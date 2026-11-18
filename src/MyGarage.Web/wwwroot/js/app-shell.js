// App shell behaviour: the light/dark toggle and the phone menu drawer. Handlers are delegated so they
// survive Blazor's enhanced navigation. A classic script (not a module) so static preview pages can load it too.
(function () {
    var STORAGE_KEY = "mg-theme";
    var OPEN_CLASS = "mg-rail-open";
    var root = document.documentElement;
    var wanted = root.getAttribute("data-bs-theme") === "dark" ? "dark" : "light";

    function syncToggles() {
        document.querySelectorAll("[data-mg-theme-toggle]").forEach(function (button) {
            button.setAttribute("aria-pressed", wanted === "dark" ? "true" : "false");
        });
        document.querySelectorAll("[data-mg-theme-set]").forEach(function (button) {
            button.setAttribute("aria-pressed", button.getAttribute("data-mg-theme-set") === wanted ? "true" : "false");
        });
    }

    function syncThemeColor() {
        var meta = document.querySelector('meta[name="theme-color"]');
        var color = window.getComputedStyle(root).getPropertyValue("--mg-bg").trim();
        if (meta && color) {
            meta.setAttribute("content", color);
        }
    }

    function setTheme(theme, persist) {
        wanted = theme;
        root.setAttribute("data-bs-theme", theme);
        if (persist) {
            try {
                window.localStorage.setItem(STORAGE_KEY, theme);
            } catch (error) {
                // Storage can be blocked; the choice then lasts until the page is reloaded.
            }
        }

        syncThemeColor();
        syncToggles();
    }

    function setRail(open) {
        document.body.classList.toggle(OPEN_CLASS, open);
        document.querySelectorAll("[data-mg-rail-toggle]").forEach(function (button) {
            button.setAttribute("aria-expanded", open ? "true" : "false");
        });

        if (open) {
            var first = document.querySelector("#mg-rail a, #mg-rail button");
            if (first) {
                // The drawer is still visibility:hidden until the next frame, which blocks focus.
                window.requestAnimationFrame(function () {
                    first.focus();
                });
            }
        }
    }

    document.addEventListener("click", function (event) {
        var target = event.target instanceof Element ? event.target : null;
        if (!target) {
            return;
        }

        if (target.closest("[data-mg-theme-toggle]")) {
            setTheme(wanted === "dark" ? "light" : "dark", true);
        } else if (target.closest("[data-mg-theme-set]")) {
            setTheme(target.closest("[data-mg-theme-set]").getAttribute("data-mg-theme-set") === "dark" ? "dark" : "light", true);
        } else if (target.closest("[data-mg-rail-toggle]")) {
            setRail(!document.body.classList.contains(OPEN_CLASS));
        } else if (target.closest("[data-mg-rail-close]") || target.closest("#mg-rail a.mg-rail-link")) {
            setRail(false);
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && document.body.classList.contains(OPEN_CLASS)) {
            setRail(false);
            var toggle = document.querySelector("[data-mg-rail-toggle]");
            if (toggle) {
                toggle.focus();
            }
        }
    });

    // Enhanced navigation can rewrite the document; put back what it removed.
    new MutationObserver(function () {
        if (root.getAttribute("data-bs-theme") !== wanted) {
            root.setAttribute("data-bs-theme", wanted);
        }
    }).observe(root, { attributes: true, attributeFilter: ["data-bs-theme"] });

    if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
        window.Blazor.addEventListener("enhancednavigationend", function () {
            syncThemeColor();
            syncToggles();
        });
    }

    var wide = window.matchMedia("(min-width: 768px)");
    var onWidthChange = function (query) {
        if (query.matches) {
            setRail(false);
        }
    };
    if (typeof wide.addEventListener === "function") {
        wide.addEventListener("change", onWidthChange);
    }

    syncThemeColor();
    syncToggles();
})();
