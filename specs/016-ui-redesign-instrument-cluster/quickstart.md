# Quickstart: validating the redesign

Run from the repository root.

## Build and test

```powershell
dotnet build MyGarage.slnx                   # zero warnings
dotnet test tests\MyGarage.Web.Tests         # no Docker needed
dotnet test MyGarage.slnx                    # all projects (Docker required; stop `aspire run` first)
```

## Look at the tokens

Open `docs/design-tokens-preview.html` in a browser. Toggle light and dark. Every row of the contrast table must read Pass.

## Look at the app

```powershell
aspire run
```

Browse to `https://localhost:7080` and sign in.

Slice 1 (foundation and shell):

1. First visit with the device set to dark: dark theme, no light flash.
2. Choose Light with the rail toggle, reload, open a private window: the choice is per browser; the private window follows the device.
3. Rail: every item opens the right page and shows as current; Drafts shows a count; Notifications shows a dot and opens the menu.
4. Open `/faq` signed out: public bar with Sign in, no rail.
5. Resize to 320 px: rail becomes a drawer opened by the menu button; closes on backdrop and Escape; nothing scrolls sideways.
6. Tab through a page: skip link first, visible focus everywhere, rail items named.
7. Enable reduced motion in the operating system: nothing animates.
8. Network panel: no request leaves the application's own origin for fonts or styles; fonts total under 250 KB.

Slice 2 (garage and vehicle):

1. The garage lists active vehicles as cards with odometer, next due, and spend this year; archived vehicles sit below with Restore.
2. A vehicle opens on Overview; Timeline shows every kind newest first and filters by kind; Specs shows "Add" links for unset values.

Slice 3 (records and reminders):

1. A service record reads as a receipt: right-aligned amounts, subtotal, tax, a large total, credits marked; each line has an Actions menu with Edit and Delete.
2. Reminders rank Overdue, Due soon, Upcoming, Mileage needed, Paused, each with a gauge; the new-reminder form shows the next due as you type; a service done twice with no reminder is suggested on the vehicle's Reminders tab.

Slice 4 (import and review):

1. Add vehicle opens on the Sticker step; "Enter manually" opens the form (also with script off). After a sticker is read, every value read is listed with a "From sticker" mark and the VIN is flagged.
2. A draft's review page shows the readiness ring beside the compatibility list; Save stays disabled until the check passes.

Slice 5 (utility pages and cleanup):

1. Preferences has an Appearance card: Light and Dark buttons keep the choice in this browser.
2. An unknown address shows the odometer resting on 404 (with reduced motion on, it does not roll), with Return Home and Search.
3. Search shows one large field, removable chips, and highlighted matches; the dashboard figures are mono readouts.
4. `dotnet test tests\MyGarage.Web.Tests` fails if `app.css` gains a hex colour or a pre-redesign `--mg-*` name.
