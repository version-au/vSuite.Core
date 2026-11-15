// vSuite.Web's helpers for a server app's page (window.vs): remembered layout (per browser, under the app's name - <html data-app>),
// the side panel's splitter, Ctrl+B, the tab strip's wheel, copy (with a fallback for plain http), focus, live logs that follow.
// The layout calls vs.start(dotnet) once; the dotnet object answers ToggleSide and SideResized(width).
window.vs = (function () {
    var app = (document.documentElement.dataset.app || "vsuite") + ".";
    function get(k) { try { return localStorage.getItem(app + k); } catch (e) { return null; } }
    function set(k, v) { try { localStorage.setItem(app + k, v); } catch (e) { } }
    var layout = null;

    function splitter(dotnet) {
        var sp = document.getElementById("splitter");
        if (!sp || sp.dataset.on) return;
        sp.dataset.on = "1";
        sp.addEventListener("pointerdown", function (e) {
            var app = sp.closest(".app");
            var startX = e.clientX, startW = app.querySelector(".side").getBoundingClientRect().width;
            sp.setPointerCapture(e.pointerId);
            sp.classList.add("drag");
            function move(ev) {
                var w = Math.max(160, Math.min(520, Math.round(startW + ev.clientX - startX)));
                app.style.setProperty("--side-w", w + "px");
                sp.dataset.w = w;
            }
            function up() {
                sp.removeEventListener("pointermove", move);
                sp.removeEventListener("pointerup", up);
                sp.classList.remove("drag");
                if (sp.dataset.w) { set("sideWidth", sp.dataset.w); dotnet.invokeMethodAsync("SideResized", parseInt(sp.dataset.w, 10)); }
            }
            sp.addEventListener("pointermove", move);
            sp.addEventListener("pointerup", up);
        });
    }

    return {
        // first render: give the layout what this browser remembered, and hook up keys / the splitter / the tab wheel
        start: function (dotnet) {
            var zone = "";
            try { zone = Intl.DateTimeFormat().resolvedOptions().timeZone || ""; } catch (e) { }
            splitter(dotnet);
            if (!layout) {
                document.addEventListener("keydown", function (e) {
                    if (e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === "b" || e.key === "B")) { e.preventDefault(); dotnet.invokeMethodAsync("ToggleSide"); }
                });
                var strip = document.getElementById("tabstrip");
                if (strip) strip.addEventListener("wheel", function (e) {
                    if (strip.scrollWidth <= strip.clientWidth) return;
                    strip.scrollLeft += (Math.abs(e.deltaY) > Math.abs(e.deltaX) ? e.deltaY : e.deltaX);
                    e.preventDefault();
                }, { passive: false });
            }
            layout = true;
            var w = parseInt(get("sideWidth") || "0", 10);
            var narrow = window.innerWidth < 760;   // a phone: the side panel starts closed (it covers the page there)
            return { zone: zone, tabs: get("tabs") || "", sideWidth: w || 240, sideOpen: narrow ? false : get("sideOpen") !== "0", narrow: narrow };
        },
        save: function (k, v) { set(k, v); },
        showTab: function () {
            var on = document.querySelector("#tabstrip .tab.on");
            if (on && on.scrollIntoView) on.scrollIntoView({ block: "nearest", inline: "nearest" });
        },
        // changes not saved: the browser asks before the tab closes
        dirty: function (on) {
            window.onbeforeunload = on ? function (e) { e.preventDefault(); e.returnValue = ""; return ""; } : null;
        },
        focus: function (id) { var el = document.getElementById(id); if (el) el.focus(); },
        scrollEnd: function (id) { var el = document.getElementById(id); if (el) { el.scrollTop = el.scrollHeight; el.dataset.stuck = "1"; } },
        // live logs: stay at the end while you're there; scroll up to read and it stops following until you're back at the end
        keepEnd: function (id) {
            var el = document.getElementById(id);
            if (!el) return;
            if (!el.dataset.watch) {
                el.dataset.watch = "1";
                el.addEventListener("scroll", function () { el.dataset.stuck = (el.scrollHeight - el.scrollTop - el.clientHeight < 40) ? "1" : "0"; });
            }
            if (el.dataset.stuck !== "0") el.scrollTop = el.scrollHeight;
        },
        // copy: the clipboard API needs https, so plain http (the LAN) falls back to a selected text box
        copy: function (text) {
            if (navigator.clipboard && window.isSecureContext) {
                return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return fallback(text); });
            }
            return fallback(text);
            function fallback(t) {
                var ta = document.createElement("textarea");
                ta.value = t;
                ta.setAttribute("readonly", "");
                ta.style.position = "fixed";
                ta.style.opacity = "0";
                document.body.appendChild(ta);
                ta.select();
                var ok = false;
                try { ok = document.execCommand("copy"); } catch (e) { }
                document.body.removeChild(ta);
                return ok;
            }
        }
    };
})();
