# Feature Specification: Discounts, Labor Totals, and Tax on Service Records

**Feature Branch**: `009-invoice-pricing-rules`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "Real invoices do not fit the current pricing rules. 1. The line item that includes -4.04 is a discount. 2. There is a possibility the labor hours and/or rate will not be provided, and you will only know the total labor cost. We need that to be acceptable. 3. Tax is going to be included as well, so the tax total is going to be needed as a potential line item."

**Refinement (2026-11-04)**: A discount is a flat cost below zero on an ordinary line item. A line item gets a "Total labor" field after "Labor hours" and "Hourly rate": it is calculated when both hours and rate are given, and it is entered directly (as a plain amount) when neither is given. Tax is a single amount on the service record itself, separate from the line items, and it is part of the record's total.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record a discount on a service record (Priority: P1)

A signed-in user adds or edits a service record and enters a line item whose flat cost is below zero, such as a coupon or loyalty discount (for example -4.04). The line item is accepted, shown as a credit in the list and on the record, and reduces the record's total.

**Why this priority**: Discounts appear on real invoices. Today a draft built from an emailed invoice cannot be accepted until the user changes the discount to something it is not, so this blocks the main email-to-record flow.

**Independent Test**: Add a service record with a 50.00 flat-cost line and a -4.04 flat-cost line and confirm the record saves with a total of 45.96; confirm the discount line is shown as a credit; confirm a flat cost below the allowed range is still rejected.

**Acceptance Scenarios**:

1. **Given** a user adding a line item, **When** they enter a description and a flat cost of -4.04, **Then** the line item is accepted and the record total drops by 4.04.
2. **Given** a record with a discount line, **When** the user views the record, **Then** the discount is shown as a credit (a minus amount), not as an error or a charge.
3. **Given** a user entering a flat cost beyond the allowed range in either direction, **When** they save, **Then** nothing is stored and the message says the allowed range.
4. **Given** discounts larger than the charges they are meant to offset, **When** the record total would be below zero, **Then** the record is not saved and the message says the total cannot be below zero.
5. **Given** a line item with a discount, **When** the user also fills in labor or parts on the same line, **Then** the existing rule applies and the line is rejected: a flat cost cannot be combined with labor or parts.

---

### User Story 2 - Record labor when only the total is known (Priority: P1)

A line item shows "Labor hours", "Hourly rate", and then "Total labor". When the user gives both hours and rate, the total labor is calculated from them. When the user gives neither, they can type the total labor directly. A line item with only a labor total (and optionally parts) is accepted.

**Why this priority**: Many shops print only a labor total. Without this the user has to turn labor into a flat cost, which cannot be combined with parts on the same line, and the line stops being identifiable as labor.

**Independent Test**: Add a line with 2.5 hours at 120.00 and confirm total labor shows 300.00 without typing it; add a line with no hours or rate and a typed total labor of 180.00 and confirm it saves as 180.00; add a line with a part and a typed total labor and confirm both count in the line total.

**Acceptance Scenarios**:

1. **Given** a user entering labor hours and an hourly rate, **When** both are present, **Then** total labor is calculated from them, shown, and cannot be typed over.
2. **Given** a user who leaves labor hours and hourly rate empty, **When** they enter a total labor amount, **Then** the line item is accepted with that labor total.
3. **Given** a line item with a typed total labor and one or more parts, **When** it is saved, **Then** the line total is the labor total plus the parts.
4. **Given** a user who enters labor hours without an hourly rate, or a rate without hours, **When** they save, **Then** nothing is stored and the message asks for both or neither.
5. **Given** a user who enters hours and a rate and also types a total labor that differs from the calculation, **When** they save, **Then** the calculated amount is used and the typed value is not kept.
6. **Given** an existing record with labor hours and rate, **When** the user opens it after this change, **Then** it shows the same labor total as before.

---

### User Story 3 - Record tax on a service record (Priority: P2)

On the service record form the user can enter the tax total for the whole record, separate from the line items. The record then shows its line items, the tax, and a total that includes the tax. Tax is optional and defaults to none.

**Why this priority**: Most invoices end with a tax amount. It is needed to make the record total match the invoice, but a record is still useful without it.

**Independent Test**: Add a record with lines totaling 100.00 and a tax of 8.25 and confirm the total shows 108.25 with the tax listed on its own line; add a record without tax and confirm it behaves as before; try a negative tax and confirm it is rejected.

**Acceptance Scenarios**:

1. **Given** a user adding a service record, **When** they enter a tax amount, **Then** the record stores it and its total is the line items plus the tax.
2. **Given** a record with tax, **When** it is listed or viewed, **Then** the tax is shown separately from the line items and the total includes it.
3. **Given** a record with no tax, **When** it is viewed, **Then** no tax line is shown and the total is the line items only.
4. **Given** a tax amount that is negative, over the allowed maximum, or has more than two decimal places, **When** the user saves, **Then** nothing is stored and the message names the problem next to the tax field.
5. **Given** existing service records created before this change, **When** they are viewed, **Then** they show no tax and the same total as before.

---

### User Story 4 - Emailed invoices fill these fields correctly (Priority: P2)

When an emailed or attached invoice is turned into a draft, discounts arrive as negative flat-cost lines, a labor amount with no hours or rate arrives as a labor total, and the tax arrives in the tax field rather than as a line item. The draft's check compares the invoice's document total with the line items plus the tax, so a correct draft no longer warns about a mismatch.

**Why this priority**: This is the reason the first three stories matter in practice, and it is what the owner hit when reviewing a real invoice.

**Independent Test**: Process an invoice with parts, labor, a discount, and tax and confirm the draft shows each in its own place, shows no mismatch warning, and can be accepted without edits; process an invoice with only a labor total and confirm no hours or rate are invented.

**Acceptance Scenarios**:

1. **Given** an invoice with a discount, **When** the draft is built, **Then** the discount is a line item with a negative flat cost and the draft's check reports it as acceptable.
2. **Given** an invoice that shows only a labor total, **When** the draft is built, **Then** that line has a labor total and no hours or rate.
3. **Given** an invoice that shows hours or a rate but not both, **When** the draft is built, **Then** the labor is kept as a total and the lone hours or rate is left out.
4. **Given** an invoice with a tax amount, **When** the draft is built, **Then** the tax is in the tax field and not in the line items.
5. **Given** a draft whose line items plus tax equal the document total, **When** it is checked, **Then** no mismatch warning is shown; **and when** they differ, **Then** the warning states both amounts as it does today.

---

### Edge Cases

- A discount line combined with parts or labor on the same line is still rejected (flat cost stays exclusive).
- A record whose lines and tax add up to less than zero is rejected; a total of exactly zero is allowed.
- A typed total labor of zero is allowed; a labor total over the allowed maximum is rejected.
- Hours and rate given with a typed total labor: the calculation wins.
- Labor hours with no rate (or the reverse) is an error in the form and is never silently dropped there; only the draft builder drops a lone value and records that it did.
- Tax has at most two decimal places and the same upper limit as other amounts; the tax field is empty by default, and empty and zero both mean no tax.
- Records saved before this change keep their meaning and totals; nothing is recalculated or rewritten.
- A record with a very large number of line items still follows the existing line-item limit.
- Rounding keeps to cents, rounding halves away from zero, as it does today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A line item's flat cost MUST be allowed to be below zero, to represent a discount, within the range -9,999,999.99 to 9,999,999.99 and with at most two decimal places.
- **FR-002**: A flat cost MUST still not be combined with labor or parts on the same line item.
- **FR-003**: A line item's labor MUST consist of labor hours, an hourly rate, and a total labor amount, in that order on the form and in displays.
- **FR-004**: When both labor hours and an hourly rate are given, total labor MUST be calculated from them and MUST NOT be editable.
- **FR-005**: When neither labor hours nor an hourly rate is given, the user MUST be able to enter total labor directly, from 0.00 to 9,999,999.99 with at most two decimal places.
- **FR-006**: Labor hours without an hourly rate, or an hourly rate without labor hours, MUST be rejected with a message asking for both or neither.
- **FR-007**: A line item MUST be acceptable when its pricing is a flat cost, or labor (calculated or entered) and/or parts, and MUST be rejected when it has none of these.
- **FR-008**: A line item's total MUST be the flat cost, or the labor total plus the parts, rounded to cents.
- **FR-009**: A service record MUST accept an optional tax amount from 0.00 to 9,999,999.99 with at most two decimal places, stored with the record and not as a line item.
- **FR-010**: A service record's total MUST be the sum of its line item totals plus its tax, and MUST NOT be below zero; a record that would total below zero MUST be rejected with a message that says so.
- **FR-011**: Service records, line items, and tax saved before this change MUST keep their values and totals unchanged.
- **FR-012**: Discounts MUST be shown as credits (a minus amount) wherever line items are listed or summarized, and the tax MUST be shown on its own line separately from the line items wherever a record is shown in full.
- **FR-013**: When an invoice is turned into a draft, discounts MUST be proposed as negative flat-cost lines, a labor amount without hours and rate MUST be proposed as a labor total, a lone labor hours or hourly rate MUST be left out of the proposal with the labor kept as a total, and the tax MUST be proposed in the tax field and not as a line item.
- **FR-014**: The draft's check MUST compare the invoice's document total with the sum of line items plus the proposed tax, MUST show the same mismatch warning as today only when they differ, and MUST not treat a discount, a labor total, or a tax amount as invalid.
- **FR-015**: Accepting a draft MUST create the service record with the same line items, labor totals, and tax that the draft showed.
- **FR-016**: Each ownership and authorization rule that applies to service records today MUST apply unchanged to the new fields; no value of the new fields may be read or written by another user.
- **FR-017**: Field-level problems in any of the new or changed fields MUST be shown next to the field, with the field identified in the message.

### Key Entities

- **Service line item**: One row of a service record. Pricing is either a flat cost (which may be negative to represent a discount) or labor and/or parts. Labor is hours, an hourly rate, and a total labor, where total labor is either calculated from the first two or entered when they are absent.
- **Service record**: A visit to a shop or person on a date at an odometer reading, with line items and an optional tax amount. Its total is the line item totals plus the tax.
- **Draft service record proposal**: The editable proposal built from an emailed invoice; it carries the same line items and tax, and the document total as printed on the invoice for comparison only.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An invoice with parts, labor, a discount, and tax can be recorded with every amount in its own place, and the record's total equals the invoice's document total.
- **SC-002**: The emailed invoice that raised these issues produces a draft with no invalid fields and no mismatch warning, and the owner can accept it without changing any amount.
- **SC-003**: 100% of service records, line items, and totals saved before this change display the same values after it.
- **SC-004**: A user can record a labor-only line (total, no hours or rate) in one step, without choosing a different pricing mode.
- **SC-005**: Every rejected amount in the new or changed fields shows a message beside the field that names the field and the allowed range or rule.
- **SC-006**: No service record, line item, or tax value is ever visible to or changeable by a user other than the vehicle's owner.

## Assumptions

- The money range, the two-decimal rule, and cent rounding that apply today continue to apply to the new amounts.
- A discount is a flat-cost line; a discount that applies to a labor or parts line is entered as a separate line item.
- A record has one tax amount; a second tax is added to the first.
- Tax and discounts apply only to service records; fuel, charging, inspections, registrations, insurance, and taxes (the other record types) are unchanged.
- The document total on a draft stays a comparison value that is never stored on the record.
- Reports and exports, if any exist later, will use the record total as defined here.
- Existing data needs no rewriting: the new rules are looser than the old ones for existing rows, and the new tax and labor total are empty for them.
