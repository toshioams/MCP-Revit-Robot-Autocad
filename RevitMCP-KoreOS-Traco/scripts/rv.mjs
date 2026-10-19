#!/usr/bin/env node
// Ad-hoc call to the Revit add-in over HTTP, for tools the MCP client has not reloaded yet.
// Usage: node scripts/rv.mjs <command> '<json params>' [--dry]
import {callRevit} from '../src/McpServer/dist/revitClient.js';
const [cmd, json = '{}', flag] = process.argv.slice(2);
const res = await callRevit(cmd, JSON.parse(json), flag === '--dry');
console.log(JSON.stringify(res, null, 1));
