# Contract: Pricing Changes

No endpoint is added or removed. Routes, authorization, status codes, and the `ProblemDetails` error shape are unchanged. Only these request and response fields change.

## Service record request (`ServiceRecordRequest`, create and update)

| Field | Change |
|---|---|
| `tax` | **new**, optional decimal, 0.00 to 9,999,999.99, at most 2 decimals |

Validation failure for `tax` is `400` with the property `Tax` and a message that names the range or the decimal rule.

## Service record response (`ServiceRecordDto`, `ServiceRecordSummaryDto`)

| Field | Change |
|---|---|
| `tax` | **new** on `ServiceRecordDto`: the stored tax, or `null` when none |
| `totalCost` | now `sum(line totals) + tax`; may be negative |

## Line item request (`ServiceLineItemRequest`)

| Field | Change |
|---|---|
| `flatCost` | range is now -9,999,999.99 to 9,999,999.99 (negative = discount) |
| `laborCost` | **new**, optional decimal, 0.00 to 9,999,999.99. Meaningful only when `laborHours` and `laborRate` are both absent; ignored when both are present; rejected together with `flatCost` |

Pricing rule messages (property `Pricing`): exactly one of a flat cost, or labor and/or parts, is required. A lone `laborHours` or `laborRate` keeps today's message: "Hourly rate is required with labor hours." / "Labor hours are required with an hourly rate."

## Line item response (`ServiceLineItemDto`)

| Field | Change |
|---|---|
| `laborCost` | now the effective labor cost: calculated from hours and rate when both are present, otherwise the stored entry, otherwise `null` |
| `totalCost` | may be negative |

## Draft proposal (`DraftProposal`, stored JSON and the draft endpoints)

| Field | Change |
|---|---|
| `serviceRecord.tax` | **new**, optional decimal |
| `serviceRecord.lineItems[].laborCost` | **new**, optional decimal |
| `serviceRecord.documentTotal` | unchanged; still the invoice's printed total, used only for the comparison warning |

Compatibility field paths: `serviceRecord.tax` is listed when a tax is proposed; `serviceRecord.lineItems[i].laborCost` is listed when labor has no hours and rate. The mismatch warning text becomes "The line items and tax add up to X, but the document total is Y." when a tax is proposed, and is unchanged otherwise.

## Extraction schema (`maintenance_draft`)

| Field | Change |
|---|---|
| `tax` | **new** top-level, `number` or `null`, required in the strict schema |
| `lineItems[].laborCost` | **new**, `number` or `null`, required in the strict schema |

Instruction text additions: a discount is a negative `flatCost`; tax is returned in `tax` and never as a line item; give `laborHours` and `laborRate` only when both are printed, otherwise give the labor total in `laborCost`.

Mapper rules (the guard on the model's answer): a lone `laborHours` or `laborRate` is dropped and the labor total kept as `laborCost`; both present means `laborCost` is dropped; a flat cost with any labor or parts is resolved as today; amounts round to cents.
