---
name: cmes-row-context-menu
description: >
  How to attach per-row actions (Copy / Edit / Delete / …) to a grid in
  CCL-MES Hybrid. Row actions are a right-click / long-press / ⋯ kebab menu via
  the shared RowContextMenu.razor — NOT an inline "Actions" column. Use when
  adding row actions to any grid, or reviewing one.
---

# CMES row context menu

**Rule (enforced):** per-row actions go in **`Shared/RowContextMenu.razor`**,
opened three ways that share ONE state + component:

1. **Right-click** the row (`@oncontextmenu:preventDefault` → open at mouse).
2. **Long-press** ~500ms (touch / WKWebView where `contextmenu` may not fire).
3. **⋯ kebab** button in a narrow trailing column (discoverable entry point).

Do **not** add an inline `<th>Actions</th>` column of Edit/Delete buttons. CI
gate: `scripts/gate-row-actions.sh` fails a new grid with an "Actions" header.
Lesson: `docs/LESSONS-LEARNED.md` **L35**. Reference impl: `NpiWorkCenters.razor`.

## Minimal example

```razor
@using CCL.MES.Hybrid.Razor.Shared

<tr @oncontextmenu="e => OpenMenuAt(row, e.ClientX, e.ClientY)" @oncontextmenu:preventDefault="true"
    @onpointerdown="e => LongPressStart(row, e)" @onpointerup="LongPressCancel" @onpointerleave="LongPressCancel">
    ... cells ...
    @if (_canAct)
    {
        <td class="wc-kebab-col">
            <button class="row-kebab" data-fw-nodrag aria-haspopup="menu" aria-label="Actions"
                    @onclick="e => OpenMenuAt(row, e.ClientX, e.ClientY)" @onclick:stopPropagation="true">⋯</button>
        </td>
    }
</tr>

<RowContextMenu Open="_menuRow is not null" Anchor="_menuAnchor" Items="_menuItems" OnClose="() => _menuRow = null" />

@code {
    void OpenMenuAt(TRow row, double x, double y)
    {
        if (!_canAct) return;                       // RBAC: no items → never open
        _menuRow = row; _menuAnchor = (x, y);
        _menuItems = new[]
        {
            new ContextMenuItem { Label = "Copy", Icon = "⎘", OnClick = EventCallback.Factory.Create(this, () => Copy(row)) },
            new ContextMenuItem { Label = "Edit", Icon = "📝", OnClick = EventCallback.Factory.Create(this, () => Edit(row)) },
            ContextMenuItem.Divider,
            new ContextMenuItem { Label = "Delete", Icon = "🗑", Danger = true, OnClick = EventCallback.Factory.Create(this, () => Delete(row)) },
        };
    }
}
```

## Checklist

- [ ] **Three entry points** share ONE menu state — no duplicated action logic.
- [ ] **RBAC by omission**: build only the items the user may perform (server
      still enforces the real 403). No permitted items → don't open + hide the ⋯.
- [ ] Menu **clamps** inside the viewport (RowContextMenu does this in JS).
- [ ] Closes on: outside-click, **Esc**, selecting an item, scroll/zoom/blur.
- [ ] a11y: `role=menu`/`menuitem`, first item focused, **Arrow Up/Down** roving,
      Enter/Space activates (native `<button>`), `aria-haspopup="menu"` on ⋯.
- [ ] Kebab ≥28px hit area, `@onclick:stopPropagation`, focus-visible ring.
- [ ] `Danger` styling for destructive items; a `Divider` before Delete.

## Do NOT

- Add an inline `<th>Actions</th>` column of Edit/Delete buttons (the gate fails it).
- Re-implement positioning/close logic per page — that's what RowContextMenu owns.
- Hand-bind a menu to one entity like the legacy `SpecContextMenu` — prefer the
  generic `RowContextMenu` + a `ContextMenuItem` list.
