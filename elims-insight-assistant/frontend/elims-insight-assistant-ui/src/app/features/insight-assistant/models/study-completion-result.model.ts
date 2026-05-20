// A single result row returned by the assistant. Columns are dynamic — they come
// straight from the primary entity's fields (plus optional classification metadata
// when the query is a timeliness query). The UI renders columns from Object.keys.
export type AssistantResultRow = Record<string, unknown>;

// Kept as an alias so existing imports continue to work.
export type StudyCompletionResult = AssistantResultRow;
