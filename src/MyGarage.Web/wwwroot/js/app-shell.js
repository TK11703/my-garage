// App shell behaviour: the light/dark toggle and the mobile menu button. Handlers are delegated so they
// survive Blazor's enhanced navigation. A classic script (not a module) so docs/theme-preview.html can load it too.
(function () {
    var STORAGE_KEY = "mg-theme";
    var OPEN_CLASS = "mg-sidebar-open";
    var root = document.documentElement;
    var wanted = root.getAttribute("data-bs-theme") === "dark" ? "dark" : "light";

    function syncToggles() {
        document.querySelectorAll("[data-mg-theme-toggle]").forEach(function (button) {
            button.setAttribute("aria-pressed", wanted === "dark" ? "true" : "false");
        });
    }

    function syncThemeColor() {
        var meta = document.querySelector('meta[name="theme-color"]');
        var color = window.getComputedStyle(root).getPropertyValue("--mg-navbar-bg").trim();
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

    function setSidebar(open) {
        document.body.classList.toggle(OPEN_CLASS, open);
        document.querySelectorAll("[data-mg-sidebar-toggle]").forEach(function (button) {
            button.setAttribute("aria-expanded", open ? "true" : "false");
        });

        if (open) {
            var first = document.querySelector("#mg-sidebar a, #mg-sidebar button");
            if (first) {
                first.focus();
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
        } else if (target.closest("[data-mg-sidebar-toggle]")) {
            setSidebar(!document.body.classList.contains(OPEN_CLASS));
        } else if (target.closest("[data-mg-sidebar-close]") || target.closest("#mg-sidebar a.mg-nav-link")) {
            setSidebar(false);
        }
    });

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && document.body.classList.contains(OPEN_CLASS)) {
            setSidebar(false);
            var toggle = document.querySelector("[data-mg-sidebar-toggle]");
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
            setSidebar(false);
        }
    };
    if (typeof wide.addEventListener === "function") {
        wide.addEventListener("change", onWidthChange);
    }

    syncThemeColor();
    syncToggles();
})();
