# AGENTS.md

## Project Context
This project is a hackathon demo for eLIMS Insight Assistant: a governed natural-language analytics assistant for eLIMS.

The assistant lets users ask analytical questions such as "Find studies not completed on time" while ensuring the LLM does not directly access databases or execute arbitrary code.

## Architecture Rules
- The LLM must only generate a proposed Markdown and JSON execution plan.
- The backend must validate the JSON plan before execution.
- The execution engine must call only approved service contracts.
- No direct database queries are allowed.
- No SQL generation or execution is allowed.
- No write/update/delete operations are allowed in the demo.
- Execution must be deterministic and auditable.
- Use deterministic mock plan generation for the first demo.
