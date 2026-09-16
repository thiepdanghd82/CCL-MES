# markitdown-mcp — MCP server

A lightweight MCP server exposing **one tool**:
`convert_to_markdown(uri)`, where `uri` may be any `http:`, `https:`, `file:`
or `data:` URI. Transports: STDIO (default), Streamable HTTP, and SSE.

> Meant for **local use with local, trusted agents.** It has no authentication
> and runs with the privileges of the user running it — `convert_to_markdown`
> can read any file that user can read and fetch anything on the network they
> can reach. In HTTP/SSE mode it binds to `localhost` by default; do not bind
> other interfaces unless you fully understand the consequences.

## Install and run

```bash
pip install markitdown-mcp

markitdown-mcp                                    # STDIO
markitdown-mcp --http --host 127.0.0.1 --port 3001  # Streamable HTTP + SSE
```

## Docker

```bash
docker build -t markitdown-mcp:latest .
docker run -it --rm markitdown-mcp:latest
```

That covers remote URIs. For local files, mount the directory:

```bash
docker run -it --rm -v /home/user/data:/workdir markitdown-mcp:latest
```

Everything under `data` is then reachable as `/workdir/...` inside the
container — `/home/user/data/example.txt` → `/workdir/example.txt`.

## Claude Desktop

The Docker image is the recommended way to run it for Claude Desktop. Edit
`claude_desktop_config.json`
(<https://modelcontextprotocol.io/quickstart/user#for-claude-desktop-users>):

```json
{
  "mcpServers": {
    "markitdown": {
      "command": "docker",
      "args": ["run", "--rm", "-i", "markitdown-mcp:latest"]
    }
  }
}
```

With a mounted directory:

```json
{
  "mcpServers": {
    "markitdown": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-v", "/home/user/data:/workdir",
        "markitdown-mcp:latest"
      ]
    }
  }
}
```

Restart the app after editing the config.

## Debugging with MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

Open the printed URL (e.g. `http://localhost:5173/`), then:

- **STDIO** — transport `STDIO`, command `markitdown-mcp`, Connect.
- **Streamable HTTP** — transport `Streamable HTTP`, URL
  `http://127.0.0.1:3001/mcp`, Connect.
- **SSE** — transport `SSE`, URL `http://127.0.0.1:3001/sse`, Connect.

Then: Tools tab → List Tools → `convert_to_markdown` → run it on any valid URI.

## Security considerations

No authentication exists. Even bound to `localhost`, any process or user on the
same machine can call it, and the tool can read any file the server's user can
read or pull any reachable network data. If you need isolation, run it in a VM
or container with tightly scoped user permissions and mounts. Above all, do not
bind to non-localhost interfaces.
