# Quickstart: Verify Discounts, Labor Totals, and Tax

## Local

1. Apply migrations to an empty database and confirm `0013` applies after `0012` (the Database tests do this against a Testcontainers database).
2. Run the suites: `dotnet test MyGarage.slnx`. Docker must be running.
3. With `aspire run`, sign in and open a vehicle, Maintenance, Service Records, then add a record:
   - Add a line "Oil change" with a flat cost of 50.00, then a line "Coupon" with a flat cost of -4.04. The record total is 45.96 and the coupon shows as a credit.
   - Add a line "Labor" with hours 2.5 and rate 120.00: Total labor shows 300.00 and cannot be edited. Clear hours and rate: Total labor becomes editable; type 180.00 and save.
   - Enter hours without a rate: the form asks for both or neither.
   - Enter a tax of 8.25 on the record: the detail shows the tax on its own line and the total includes it. Clear the tax: the tax line disappears.
4. Open a record created before this change: values and total are unchanged and no tax line is shown.

## After deployment (spec 008 environment)

1. Merge the pull request; the pipeline deploys after the owner approves the `production` gate. Migration `0013` is applied by the API on start.
2. Forward the invoice that raised these issues to the intake mailbox again, or open its existing draft. The draft shows the discount as a negative flat-cost line, the labor as a total, the tax in the tax field, no mismatch warning, and the draft can be accepted without edits.
3. Accept it and confirm the record's total equals the invoice's printed total.

## Failure checks

- A flat cost of -10,000,000.00 is rejected with the allowed range.
- A negative tax is rejected next to the tax field.
- A discount line with labor hours on the same line is rejected with the existing "flat cost or labor and parts" message.
