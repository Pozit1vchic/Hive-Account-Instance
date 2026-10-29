---
name: hive-control
description: Use Hive's local MCP tools to inspect Roblox account status or queue an explicitly requested account and place on this Windows PC.
---

# Hive control

Hive must be open as the same Windows user as the MCP client. Connect the local stdio server at `artifacts/Hive/Mcp/Hive.Mcp.exe` (or the `Mcp` folder of the installed release).

Call `list_accounts` for account IDs and linked process state. Call `launch_account` only for the accounts and Place ID requested by the user; provide a Job ID only if requested. A queued result is not proof of a successful join: check status afterward. Never request or expose passwords, cookies, launch tickets, or private links in the conversation or tool inputs. The local MCP has no phone or internet access.
