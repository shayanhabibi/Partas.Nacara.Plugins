// Partas.Nacara.Plugins.Solid: mounts each example into the placeholder the build left for it.
//
// A page's examples are one bundle beside this file, named by the key on the placeholder, and
// export mount(cell, element). A classic deferred script, so document.currentScript still says
// where it was served from, and the placeholders are all parsed by the time it runs.
//
// A JSX panel is a <details> the build left under a cell. Opened, it is filled from the page's
// <key>.jsx.json, fetched once for all of the page's panels.
(() => {
    const base = document.currentScript ? document.currentScript.src : location.href;
    const placeholders = document.querySelectorAll("[data-partas-cell]");
    const panels = document.querySelectorAll("[data-partas-jsx-cell]");
    if (placeholders.length === 0 && panels.length === 0) return;

    const style = document.createElement("style");
    style.textContent = `
.partas-solid { margin-block: 1rem; padding: 1rem; border: 1px solid color-mix(in oklab, currentColor 20%, transparent); border-radius: .5rem; }
.partas-solid:empty::before { content: "Loading example..."; opacity: .6; }
.partas-solid.partas-solid--inline { display: contents; margin: 0; padding: 0; border: 0; }
.partas-solid.partas-solid--inline:empty::before { content: none; }
.partas-solid__error { margin: 0; white-space: pre-wrap; color: #dc2626; font-size: .875em; }
.partas-solid__jsx { margin-block: 1rem; }
.partas-solid__jsx > summary { cursor: pointer; font-size: .875em; opacity: .8; }
.partas-solid__jsx > pre { margin: .5rem 0 0; padding: 1rem; overflow-x: auto; font-size: .875em; border: 1px solid color-mix(in oklab, currentColor 20%, transparent); border-radius: .5rem; }
`;
    document.head.append(style);

    const fail = (element, error) => {
        console.error(error);
        // Inline, the page reads on around it, so a line of code says enough; the console has the rest.
        if (element.classList.contains("partas-solid--inline")) {
            const note = document.createElement("code");
            note.className = "partas-solid__error";
            note.textContent = `${error && error.message ? error.message : error}`;
            element.replaceChildren(note);
            return;
        }
        const box = document.createElement("pre");
        box.className = "partas-solid__error";
        box.textContent = `This example did not run.\n${error && error.stack ? error.stack : error}`;
        element.replaceChildren(box);
    };

    const pages = new Map();
    for (const element of placeholders) {
        const page = element.dataset.partasPage;
        if (!pages.has(page)) pages.set(page, []);
        pages.get(page).push(element);
    }

    for (const [page, elements] of pages) {
        import(new URL(`${page}.js`, base).href).then(
            module => {
                for (const element of elements) {
                    try {
                        element.replaceChildren();
                        module.mount(element.dataset.partasCell, element);
                    } catch (error) {
                        fail(element, error);
                    }
                }
            },
            error => elements.forEach(element => fail(element, error)));
    }

    const jsx = new Map();
    const fill = panel => {
        const page = panel.dataset.partasJsxPage;
        if (!jsx.has(page)) {
            jsx.set(page, fetch(new URL(`${page}.jsx.json`, base).href).then(response => {
                if (!response.ok) throw new Error(`${response.status} ${response.statusText}`);
                return response.json();
            }));
        }
        const code = document.createElement("code");
        code.className = "language-jsx";
        const pre = document.createElement("pre");
        pre.append(code);
        panel.append(pre);
        code.textContent = "Loading JSX...";
        jsx.get(page).then(
            cells => { code.textContent = cells[panel.dataset.partasJsxCell] ?? "No JSX for this cell."; },
            error => { code.textContent = `The JSX did not load.\n${error}`; });
    };

    for (const panel of panels) {
        const opened = () => {
            if (!panel.open) return;
            panel.removeEventListener("toggle", opened);
            fill(panel);
        };
        panel.addEventListener("toggle", opened);
        opened();
    }
})();
