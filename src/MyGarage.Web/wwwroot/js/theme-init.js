// Picks the colour theme before the first paint so the page never flashes the wrong one.
// The choice lives only in this browser's localStorage; a new or private window starts from the system setting.
(function () {
    var theme = null;
    try {
        theme = window.localStorage.getItem("mg-theme");
    } catch (error) {
        theme = null;
    }

    if (theme !== "light" && theme !== "dark") {
        theme = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    document.documentElement.setAttribute("data-bs-theme", theme);
})();
