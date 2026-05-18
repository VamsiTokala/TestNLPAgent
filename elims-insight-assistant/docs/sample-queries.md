# Sample Queries

All queries below work with Gemini, OpenAI, and Mock generators unless noted.

## Timeliness Queries (default: shows Delayed + Indeterminate)

```
Find studies not completed on time
Show me studies that missed their deadline
Which trials are overdue?
List studies completed late
```

## Delayed Only

```
Show delayed studies
Show only delayed studies
Filter studies with classification Delayed
Which studies are delayed?
```

## Indeterminate Only

```
Show indeterminate studies
Filter studies with classification Indeterminate
Which studies are missing completion dates?
Show studies with no completion data
```

## On Time Only

```
Show on time studies
Filter studies with classification On Time
Which studies completed successfully?
```

## All Studies

```
Show all studies
Show all studies with their classification
```

## Unsupported (returns UnsupportedQuery, no results)

```
Show me all customers
What is the weather today?
List all users
```

## Notes on Mock Mode

The Mock generator matches on keywords, not full natural language understanding.
It recognises these terms:
- `not completed on time`, `delayed studies`, `completed late`, `not on time`
- `indeterminate`, `classification indeterminate`, `classification delayed`, `classification on time`
- `filter studies`, `show delayed`, `show indeterminate`, `show on time`, `show all studies`

With Gemini or OpenAI, any reasonable paraphrase of the above is understood.
