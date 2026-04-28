# eLIMS Insight Assistant: Governed Natural-Language Analytics

Hackathon demo implementing a governed NL analytics assistant.

## Solution overview
- Mock deterministic plan generation from natural language query.
- JSON plan validation against allowlisted services/fields/operators/aggregates.
- Deterministic read-only execution over seeded Study and TestP data.
- Audit record persistence in-memory and lookup by trace ID.
- Angular UI scaffold for query submission and display.

## Why LLM does not execute directly
The plan generator only proposes a markdown and JSON plan. Execution happens only after backend validation and only through approved demo service contracts.

## Seed-data expected output
- ST-001: On Time
- ST-002: Delayed
- ST-003: On Time
- ST-004: Indeterminate

Default query returns ST-002 and ST-004.
