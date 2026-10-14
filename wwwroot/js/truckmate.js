(function () {
    "use strict";

    function antiForgeryToken() {
        var el = document.querySelector("#antiforgery-token");
        return el ? el.value : "";
    }

    function fmtKg(w) {
        if (w === null || w === undefined) return "-- kg";
        return Number(w).toLocaleString("en-US", { maximumFractionDigits: 3 }) + " kg";
    }

    function applySnapshot(snap) {
        var readout = document.querySelector("#scale-readout");
        var stateChip = document.querySelector("#scale-state");
        var msg = document.querySelector("#scale-message");
        var proceed = document.querySelector("#proceed-btn");
        var gateMsg = document.querySelector("#gate-message");
        var gateOk = document.querySelector("#gate-ok");

        if (readout) readout.innerHTML = fmtKg(snap.weight) + ' <small>kg</small>';
        if (stateChip) {
            stateChip.className = "scale-state " + (snap.online ? (snap.isZero ? "zero" : "notzero") : "nosignal");
            stateChip.innerHTML = '<span class="dot"></span> ' + snap.state;
        }
        if (msg) msg.textContent = snap.message;

        ["Barrier Left", "Barrier Right", "Traffic Light", "Camera", "Indicator"].forEach(function (n) {
            var st = null;
            switch (n) {
                case "Barrier Left": st = snap.barrierLeft; break;
                case "Barrier Right": st = snap.barrierRight; break;
                case "Traffic Light": st = snap.trafficLight; break;
                case "Camera": st = snap.camera; break;
                case "Indicator": st = snap.indicator; break;
            }
            var el = document.querySelector('.hw-item[data-device="' + n + '"] .status');
            if (el) {
                el.textContent = st.status;
                el.className = "status " + (st.ready ? "ready" : "offline");
            }
        });

        if (proceed) proceed.disabled = !(snap.online && snap.isZero);
        if (gateMsg && gateOk) {
            gateOk.classList.toggle("d-none", !(snap.online && snap.isZero));
            gateMsg.classList.toggle("d-none", snap.online && snap.isZero);
        }

        document.querySelectorAll("[data-scale-widget]").forEach(function (w) {
            w.textContent = fmtKg(snap.weight);
        });
    }

    async function refreshScale() {
        if (!document.querySelector("#scale-widget")) return;
        try {
            var r = await fetch("/api/scale/snapshot", { headers: { "Accept": "application/json" } });
            if (r.ok) applySnapshot(await r.json());
        } catch (e) { }
    }

    async function postSimulate(fields) {
        var body = new URLSearchParams(fields);
        body.append("__RequestVerificationToken", antiForgeryToken());
        var r = await fetch("/api/scale/simulate", {
            method: "POST",
            headers: { "Content-Type": "application/x-www-form-urlencoded" },
            body: body
        });
        if (r.ok) applySnapshot(await r.json());
    }

    function toggleMaterialAdd(show) {
        var sel = document.querySelector("#material-select");
        var addRow = document.querySelector("#material-add");
        if (!sel || !addRow) return;
        if (show) {
            sel.classList.add("d-none");
            addRow.classList.remove("d-none");
            document.querySelector("#material-new").focus();
        } else {
            sel.classList.remove("d-none");
            addRow.classList.add("d-none");
            if (sel.value === "__new__") sel.value = "";
        }
    }

    async function addMaterial() {
        var inp = document.querySelector("#material-new");
        var name = (inp.value || "").trim();
        if (!name) return;
        var body = new URLSearchParams({ name: name });
        body.append("__RequestVerificationToken", antiForgeryToken());
        var r;
        try {
            r = await fetch("/Operator/AddMaterial", {
                method: "POST",
                headers: { "Content-Type": "application/x-www-form-urlencoded", "Accept": "application/json" },
                body: body
            });
        } catch (e) {
            alert("Could not add the material. Try again.");
            return;
        }
        if (!r.ok) {
            alert("Could not add the material. Try again.");
            return;
        }
        var m = await r.json();
        var sel = document.querySelector("#material-select");
        var opt = sel.querySelector('option[value="' + m.id + '"]');
        if (!opt) {
            opt = document.createElement("option");
            opt.value = m.id;
            opt.textContent = m.name;
            sel.insertBefore(opt, sel.querySelector('option[value="__new__"]'));
        }
        sel.value = String(m.id);
        toggleMaterialAdd(false);
    }

    window.TruckMate = {
        refreshScale: refreshScale,
        postSimulate: postSimulate
    };

    document.addEventListener("DOMContentLoaded", function () {
        refreshScale();
        setInterval(refreshScale, 1200);

        var sidebar = document.getElementById("tmSidebar");
        var backdrop = document.getElementById("tmBackdrop");
        var burger = document.getElementById("tmBurger");
        var mobileQuery = window.matchMedia("(max-width: 992px)");

        function closeSidebar() {
            if (sidebar) sidebar.classList.remove("open");
            if (backdrop) backdrop.classList.remove("show");
        }

        try {
            if (localStorage.getItem("tmSidebarCollapsed") === "1") {
                document.body.classList.add("tm-collapsed");
            }
        } catch (e) { }

        if (burger && sidebar) {
            burger.addEventListener("click", function () {
                if (mobileQuery.matches) {
                    sidebar.classList.toggle("open");
                    if (backdrop) backdrop.classList.toggle("show");
                } else {
                    var collapsed = document.body.classList.toggle("tm-collapsed");
                    try { localStorage.setItem("tmSidebarCollapsed", collapsed ? "1" : "0"); } catch (e) { }
                }
            });
        }
        if (backdrop) backdrop.addEventListener("click", closeSidebar);
        if (sidebar) {
            sidebar.querySelectorAll(".nav-link").forEach(function (a) {
                a.addEventListener("click", closeSidebar);
            });
        }

        var themeBtn = document.getElementById("tmTheme");
        if (themeBtn) {
            themeBtn.addEventListener("click", function () {
                var html = document.documentElement;
                var next = html.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
                html.setAttribute("data-bs-theme", next);
                try { localStorage.setItem("tmTheme", next); } catch (e) { }
            });
        }

        document.addEventListener("click", function (e) {
            var sim = e.target.closest("[data-sim-weight]");
            if (sim) {
                postSimulate({ weight: sim.getAttribute("data-sim-weight") });
                return;
            }
            var off = e.target.closest("[data-sim-online]");
            if (off) {
                postSimulate({ online: off.getAttribute("data-sim-online") });
                return;
            }
            var custom = e.target.closest("[data-sim-custom]");
            if (custom) {
                var inp = document.querySelector(e.target.closest("[data-sim-custom]").getAttribute("data-sim-custom"));
                if (!inp) return;
                var w = parseFloat(inp.value);
                if (!isNaN(w)) postSimulate({ weight: w });
                return;
            }
            var addNew = e.target.closest("#material-add-btn");
            if (addNew) {
                addMaterial();
                return;
            }
            var addCancel = e.target.closest("#material-add-cancel");
            if (addCancel) {
                toggleMaterialAdd(false);
                return;
            }
        });

        var matSel = document.querySelector("#material-select");
        if (matSel) {
            matSel.addEventListener("change", function () {
                if (matSel.value === "__new__") toggleMaterialAdd(true);
            });
        }

        var matEnter = document.querySelector("[data-material-enter]");
        if (matEnter) {
            matEnter.addEventListener("keydown", function (e) {
                if (e.key === "Enter") {
                    e.preventDefault();
                    addMaterial();
                }
            });
        }

        var customEnter = document.querySelector("[data-sim-enter]");
        if (customEnter) {
            customEnter.addEventListener("keydown", function (e) {
                if (e.key === "Enter") {
                    e.preventDefault();
                    var w = parseFloat(customEnter.value);
                    if (!isNaN(w)) postSimulate({ weight: w });
                }
            });
        }
    });
})();