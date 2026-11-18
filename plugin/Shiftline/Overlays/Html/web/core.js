'use strict';
// Shared code of the HTML overlays: messages from C#, one clock, one drawing loop with the overlay frame rate, frame counting,
// edit mode. C# computes everything; a page only draws what it gets. Every animation must run in this loop (SL.draw), not as
// CSS transitions, so the frame rate of General holds.
const SL = (() => {
    const handlers = {};
    const drawers = [];
    const stats = { frames: 0, gap: 0, last: 0, since: 0 };
    let cap = 0;            // most frames per second to draw (0 = every frame the browser offers)
    let lastDrawn = 0;
    let label = null;

    // seconds since 1970 (UTC), the same clock C# uses for the times it sends
    const now = () => (performance.timeOrigin + performance.now()) / 1000;

    const on = (type, handler) => { handlers[type] = handler; };
    const draw = (drawer) => { drawers.push(drawer); };
    const send = (message) => { if (window.chrome && chrome.webview) { chrome.webview.postMessage(message); } };

    function frame(stamp) {
        if (cap > 0 && lastDrawn > 0 && stamp - lastDrawn < 1000 / cap - 2) {
            // skip this frame: nothing changes on the page, so the browser does not draw it either
            requestAnimationFrame(frame);
            return;
        }
        lastDrawn = stamp;
        if (stats.last > 0) {
            stats.gap = Math.max(stats.gap, stamp - stats.last);
        }
        stats.last = stamp;
        stats.frames++;
        if (stats.since === 0) {
            stats.since = stamp;
        } else if (stamp - stats.since >= 1000) {
            // once a second: how often the browser drew and its longest pause
            send({ type: 'stats', fps: stats.frames * 1000 / (stamp - stats.since), gap: stats.gap });
            stats.frames = 0;
            stats.gap = 0;
            stats.since = stamp;
        }

        const t = now();
        for (const drawer of drawers) {
            drawer(t);
        }
        requestAnimationFrame(frame);
    }

    function setEdit(message) {
        const body = document.body;
        body.classList.toggle('sl-edit', !!message.on);
        body.classList.toggle('sl-selected', !!message.selected);
        body.classList.toggle('sl-fixed', !!message.fixed);
        if (label && message.name) {
            label.textContent = message.name;
        }
    }

    if (window.chrome && chrome.webview) {
        chrome.webview.addEventListener('message', (event) => {
            const message = event.data;
            if (!message) {
                return;
            }
            if (message.type === 'edit') {
                setEdit(message);
            } else if (message.type === 'cap') {
                cap = message.fps || 0;
            } else if (handlers[message.type]) {
                handlers[message.type](message);
            }
        });
    }

    // Edit mode: tell C# where the mouse was pressed; C# follows the cursor on screen until the button is released.
    window.addEventListener('pointerdown', (event) => {
        if (!document.body.classList.contains('sl-edit') || event.button !== 0) {
            return;
        }
        const grip = event.target.closest('.sl-grip');
        const part = !grip ? 'body' : grip.classList.contains('sl-grip-side') ? 'side' : 'grip';
        send({ type: 'press', part });
        event.preventDefault();
    });

    // Edit mode: the arrow keys move the selected overlay (Shift: 10 steps).
    window.addEventListener('keydown', (event) => {
        if (document.body.classList.contains('sl-edit') && event.key.startsWith('Arrow')) {
            send({ type: 'key', key: event.key, shift: event.shiftKey });
            event.preventDefault();
        }
    });

    window.addEventListener('DOMContentLoaded', () => {
        const add = (className) => {
            const element = document.createElement('div');
            element.className = className;
            document.body.appendChild(element);
            return element;
        };
        add('sl-edit-frame');
        label = add('sl-edit-label');
        add('sl-grip sl-grip-corner');
        add('sl-grip sl-grip-bottom');
        add('sl-grip sl-grip-side');
        requestAnimationFrame(frame);
    });

    return { now, on, draw, send };
})();
