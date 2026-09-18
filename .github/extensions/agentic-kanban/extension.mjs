import { createServer } from "node:http";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { homedir } from "node:os";
import { createCanvas, joinSession, CanvasError } from "@github/copilot-sdk/extension";

const extensionName = "agentic-kanban";
const defaultColumns = ["backlog", "ready", "in_progress", "review", "done"];
const columnLabels = {
    backlog: "Backlog",
    ready: "Ready",
    in_progress: "In progress",
    review: "Review",
    done: "Done",
};
const servers = new Map();
const boardQueues = new Map();
let copilotSession;

function copilotHome() {
    return process.env.COPILOT_HOME || join(homedir(), ".copilot");
}

function boardIdFromInput(input) {
    const raw = typeof input?.boardId === "string" && input.boardId.trim() ? input.boardId.trim() : "default";
    return raw.replace(/[^A-Za-z0-9._-]/g, "-").slice(0, 80) || "default";
}

function boardPath(boardId) {
    const sessionId = copilotSession?.sessionId || "unknown-session";
    return join(copilotHome(), "session-state", sessionId, "extensions", extensionName, "artifacts", `${boardId}.json`);
}

function nowIso() {
    return new Date().toISOString();
}

function newCardId() {
    return `card-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
}

function normalizeColumn(column) {
    const value = typeof column === "string" && column.trim() ? column.trim() : "backlog";
    if (!defaultColumns.includes(value)) {
        throw new CanvasError("invalid_column", `Column must be one of: ${defaultColumns.join(", ")}`);
    }
    return value;
}

async function readBoard(boardId) {
    const filePath = boardPath(boardId);
    try {
        const text = await readFile(filePath, "utf8");
        const board = JSON.parse(text);
        return {
            version: 1,
            columns: Array.isArray(board.columns) ? board.columns : defaultColumns,
            cards: Array.isArray(board.cards) ? board.cards : [],
        };
    } catch (error) {
        if (error?.code !== "ENOENT") {
            throw error;
        }
        return { version: 1, columns: defaultColumns, cards: [] };
    }
}

async function writeBoard(boardId, board) {
    const filePath = boardPath(boardId);
    await mkdir(dirname(filePath), { recursive: true });
    await writeFile(filePath, `${JSON.stringify(board, null, 2)}\n`, "utf8");
}

function enqueueBoardMutation(boardId, operation) {
    const previous = boardQueues.get(boardId) || Promise.resolve();
    const run = previous.catch(() => {}).then(operation);
    const cleanup = run.finally(() => {
        if (boardQueues.get(boardId) === cleanup) {
            boardQueues.delete(boardId);
        }
    });
    boardQueues.set(boardId, cleanup);
    return run;
}

async function mutateBoard(boardId, mutator) {
    return enqueueBoardMutation(boardId, async () => {
        const board = await readBoard(boardId);
        const result = mutator(board);
        await writeBoard(boardId, board);
        return { board, result };
    });
}

function cardCounts(board) {
    return Object.fromEntries(defaultColumns.map((column) => [
        column,
        board.cards.filter((card) => card.column === column).length,
    ]));
}

async function createCard(boardId, input) {
    const title = typeof input?.title === "string" ? input.title.trim() : "";
    if (!title) {
        throw new CanvasError("missing_title", "Card title is required.");
    }

    const column = normalizeColumn(input?.column);
    const timestamp = nowIso();
    const card = {
        id: newCardId(),
        title,
        description: typeof input?.description === "string" ? input.description.trim() : "",
        assignee: typeof input?.assignee === "string" ? input.assignee.trim() : "",
        column,
        createdAt: timestamp,
        updatedAt: timestamp,
    };

    const { board } = await mutateBoard(boardId, (draft) => {
        draft.cards.push(card);
        return card;
    });

    return { card, counts: cardCounts(board) };
}

async function assignCard(boardId, input) {
    const cardId = typeof input?.cardId === "string" ? input.cardId.trim() : "";
    if (!cardId) {
        throw new CanvasError("missing_card_id", "Card id is required.");
    }

    const assignee = typeof input?.assignee === "string" ? input.assignee.trim() : "";
    const { board, result } = await mutateBoard(boardId, (draft) => {
        const card = draft.cards.find((item) => item.id === cardId);
        if (!card) {
            throw new CanvasError("card_not_found", `No card found with id '${cardId}'.`);
        }
        card.assignee = assignee;
        card.updatedAt = nowIso();
        return card;
    });

    return { card: result, counts: cardCounts(board) };
}

async function moveCard(boardId, input) {
    const cardId = typeof input?.cardId === "string" ? input.cardId.trim() : "";
    if (!cardId) {
        throw new CanvasError("missing_card_id", "Card id is required.");
    }

    const column = normalizeColumn(input?.column);
    const { board, result } = await mutateBoard(boardId, (draft) => {
        const card = draft.cards.find((item) => item.id === cardId);
        if (!card) {
            throw new CanvasError("card_not_found", `No card found with id '${cardId}'.`);
        }
        card.column = column;
        card.updatedAt = nowIso();
        return card;
    });

    return { card: result, counts: cardCounts(board) };
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, (char) => ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        "\"": "&quot;",
        "'": "&#39;",
    }[char]));
}

function renderHtml(boardId) {
    const options = defaultColumns
        .map((column) => `<option value="${column}">${escapeHtml(columnLabels[column])}</option>`)
        .join("");

    return `<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Agentic Kanban</title>
    <style>
      :root { color-scheme: light dark; }
      body {
        margin: 0;
        background: var(--background-color-default, #ffffff);
        color: var(--text-color-default, #1f2328);
        font-family: var(--font-sans, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif);
        font-size: var(--text-body-medium, 14px);
        line-height: var(--leading-body-medium, 20px);
      }
      header {
        border-bottom: 1px solid var(--border-color-default, #d0d7de);
        padding: 16px;
      }
      h1 { font-size: 20px; margin: 0 0 4px; }
      .muted { color: var(--text-color-muted, #57606a); }
      form {
        display: grid;
        gap: 8px;
        grid-template-columns: minmax(160px, 1fr) minmax(120px, 180px) minmax(120px, 180px) auto;
        padding: 16px;
      }
      input, select, button {
        border: 1px solid var(--border-color-default, #d0d7de);
        border-radius: 6px;
        box-sizing: border-box;
        font: inherit;
        padding: 8px 10px;
      }
      button {
        background: var(--button-primary-background, #1f883d);
        color: var(--button-primary-foreground, #ffffff);
        cursor: pointer;
        font-weight: var(--font-weight-semibold, 600);
      }
      .board {
        display: grid;
        gap: 12px;
        grid-template-columns: repeat(5, minmax(180px, 1fr));
        overflow-x: auto;
        padding: 0 16px 16px;
      }
      .column {
        background: var(--background-color-muted, #f6f8fa);
        border: 1px solid var(--border-color-default, #d0d7de);
        border-radius: 8px;
        min-height: 240px;
        padding: 10px;
      }
      .column h2 {
        align-items: center;
        display: flex;
        font-size: 14px;
        justify-content: space-between;
        margin: 0 0 10px;
      }
      .count {
        background: var(--background-color-default, #ffffff);
        border: 1px solid var(--border-color-default, #d0d7de);
        border-radius: 999px;
        min-width: 22px;
        padding: 0 6px;
        text-align: center;
      }
      .card {
        background: var(--background-color-default, #ffffff);
        border: 1px solid var(--border-color-default, #d0d7de);
        border-radius: 8px;
        margin-bottom: 8px;
        padding: 10px;
      }
      .card-title { font-weight: var(--font-weight-semibold, 600); }
      .card-id {
        color: var(--text-color-muted, #57606a);
        font-family: var(--font-mono, "SFMono-Regular", Consolas, "Liberation Mono", monospace);
        font-size: 11px;
      }
      .empty { color: var(--text-color-muted, #57606a); font-style: italic; }
      @media (max-width: 900px) {
        form { grid-template-columns: 1fr; }
        .board { grid-template-columns: repeat(5, 240px); }
      }
    </style>
  </head>
  <body>
    <header>
      <h1>Agentic Kanban</h1>
      <div class="muted">Board: <code>${escapeHtml(boardId)}</code>. Use canvas actions to create, assign, and move cards.</div>
    </header>
    <form id="create-card">
      <input name="title" placeholder="Card title" required />
      <input name="assignee" placeholder="Assignee" />
      <select name="column">${options}</select>
      <button type="submit">Create card</button>
    </form>
    <main id="board" class="board" aria-live="polite"></main>
    <script>
      const columns = ${JSON.stringify(defaultColumns)};
      const labels = ${JSON.stringify(columnLabels)};

      async function loadBoard() {
        const response = await fetch("/state");
        const board = await response.json();
        renderBoard(board);
      }

      function renderBoard(board) {
        const root = document.getElementById("board");
        root.innerHTML = columns.map((column) => {
          const cards = board.cards.filter((card) => card.column === column);
          return \`<section class="column">
            <h2><span>\${labels[column]}</span><span class="count">\${cards.length}</span></h2>
            \${cards.length ? cards.map(renderCard).join("") : '<div class="empty">No cards</div>'}
          </section>\`;
        }).join("");
      }

      function renderCard(card) {
        return \`<article class="card">
          <div class="card-title">\${escapeHtml(card.title)}</div>
          \${card.description ? \`<p>\${escapeHtml(card.description)}</p>\` : ""}
          <div class="muted">\${card.assignee ? "Assigned to " + escapeHtml(card.assignee) : "Unassigned"}</div>
          <div class="card-id">\${escapeHtml(card.id)}</div>
        </article>\`;
      }

      function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (char) => ({
          "&": "&amp;",
          "<": "&lt;",
          ">": "&gt;",
          '"': "&quot;",
          "'": "&#39;",
        }[char]));
      }

      document.getElementById("create-card").addEventListener("submit", async (event) => {
        event.preventDefault();
        const form = event.currentTarget;
        const formData = new FormData(form);
        await fetch("/cards", {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify(Object.fromEntries(formData)),
        });
        form.reset();
        await loadBoard();
      });

      loadBoard();
      setInterval(loadBoard, 2000);
    </script>
  </body>
</html>`;
}

async function readRequestJson(req) {
    const chunks = [];
    for await (const chunk of req) {
        chunks.push(chunk);
    }
    return chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : {};
}

function sendJson(res, statusCode, value) {
    res.writeHead(statusCode, { "content-type": "application/json; charset=utf-8" });
    res.end(JSON.stringify(value));
}

async function startServer(instanceId, boardId) {
    const server = createServer(async (req, res) => {
        try {
            const url = new URL(req.url || "/", "http://127.0.0.1");
            if (req.method === "GET" && url.pathname === "/") {
                res.writeHead(200, { "content-type": "text/html; charset=utf-8" });
                res.end(renderHtml(boardId));
                return;
            }
            if (req.method === "GET" && url.pathname === "/state") {
                sendJson(res, 200, await readBoard(boardId));
                return;
            }
            if (req.method === "POST" && url.pathname === "/cards") {
                sendJson(res, 201, await createCard(boardId, await readRequestJson(req)));
                return;
            }
            sendJson(res, 404, { error: "not_found" });
        } catch (error) {
            sendJson(res, 500, { error: error?.message || "Unexpected error" });
        }
    });

    await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
    const address = server.address();
    const port = typeof address === "object" && address ? address.port : 0;
    return { boardId, server, url: `http://127.0.0.1:${port}/` };
}

function boardIdForInstance(ctx) {
    return servers.get(ctx.instanceId)?.boardId || boardIdFromInput(ctx.input);
}

copilotSession = await joinSession({
    canvases: [
        createCanvas({
            id: "agentic-kanban",
            displayName: "Agentic Kanban",
            description: "A kanban board canvas with agent actions for creating, assigning, and moving cards.",
            inputSchema: {
                type: "object",
                properties: {
                    boardId: {
                        type: "string",
                        description: "Stable board identifier. Defaults to 'default'.",
                    },
                },
                additionalProperties: false,
            },
            actions: [
                {
                    name: "get_board",
                    description: "Return the current kanban board state.",
                    handler: async (ctx) => {
                        const boardId = boardIdForInstance(ctx);
                        const board = await readBoard(boardId);
                        return { boardId, board, counts: cardCounts(board) };
                    },
                },
                {
                    name: "create_card",
                    description: "Create a kanban card on the board.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            title: { type: "string" },
                            description: { type: "string" },
                            assignee: { type: "string" },
                            column: { type: "string", enum: defaultColumns },
                        },
                        required: ["title"],
                        additionalProperties: false,
                    },
                    handler: async (ctx) => createCard(boardIdForInstance(ctx), ctx.input),
                },
                {
                    name: "assign_card",
                    description: "Assign or unassign a kanban card.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            cardId: { type: "string" },
                            assignee: { type: "string" },
                        },
                        required: ["cardId", "assignee"],
                        additionalProperties: false,
                    },
                    handler: async (ctx) => assignCard(boardIdForInstance(ctx), ctx.input),
                },
                {
                    name: "move_card",
                    description: "Move a kanban card to another column.",
                    inputSchema: {
                        type: "object",
                        properties: {
                            cardId: { type: "string" },
                            column: { type: "string", enum: defaultColumns },
                        },
                        required: ["cardId", "column"],
                        additionalProperties: false,
                    },
                    handler: async (ctx) => moveCard(boardIdForInstance(ctx), ctx.input),
                },
            ],
            open: async (ctx) => {
                const boardId = boardIdFromInput(ctx.input);
                let entry = servers.get(ctx.instanceId);
                if (!entry || entry.boardId !== boardId) {
                    if (entry) {
                        await new Promise((resolve) => entry.server.close(() => resolve()));
                    }
                    entry = await startServer(ctx.instanceId, boardId);
                    servers.set(ctx.instanceId, entry);
                }
                return {
                    title: "Agentic Kanban",
                    status: `Board: ${boardId}`,
                    url: entry.url,
                };
            },
            onClose: async (ctx) => {
                const entry = servers.get(ctx.instanceId);
                if (entry) {
                    servers.delete(ctx.instanceId);
                    await new Promise((resolve) => entry.server.close(() => resolve()));
                }
            },
        }),
    ],
});
