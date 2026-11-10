# Pipeline commands

These commands serve what the HTTP API's `/api/progress` and `/api/commands` serve, so a client of the Pipeline protocol reads them without HTTP. Both are immediate: they answer at once while another command runs.

### `pipeline progress`
Get the progress of the running command, as `/api/progress` serves it: whether a command runs, and what it last reported.

No parameters.

**Returns:** `object` — `{"active":false,"progress":null}` while no command runs; otherwise `{"active":true,"progress":{"title","info","current","total","pct"}}`, with `progress` null until the command or the Editor reports any.
**Notes:** `MainThreadRequired = false`. Immediate.

### `pipeline list_commands`
List the registered commands, filtered, sorted and grouped as `/api/commands` serves them.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `detail` | no | `full` | `full` with parameters and schemas, `compact`, `tags` (an index of tags) or `groups` (an index of groups). |
| `query` | no | `–` | Keep the commands whose full name, description or a tag contains this, ignoring case. |
| `group` | no | `–` | Keep a group and its subgroups. |
| `tag` | no | `–` | Keep a tag and the tags beneath it. |
| `include-tests` | no | `false` | Also list the commands the test assemblies declare. |
| `group-by` | no | `flat` | `flat`, `package`, `tag` or `group`. |
| `sort` | no | `name` | `name` or `package`. |
| `order` | no | `asc` | `asc` or `desc`. |
| `offset` | no | `0` | How many to skip, after sorting. |
| `limit` | no | `–` | How many to return at most; all by default. |

**Returns:** `object` — the `/api/commands` answer for the same options, without its `server` block.
**Notes:** `MainThreadRequired = false`. Immediate. `detail=obsolete_names` stays on `/api/commands`. A value that is unknown, or can't be combined with another, fails with `InvalidUsage`.
