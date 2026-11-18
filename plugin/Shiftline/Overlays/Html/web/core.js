'use strict';
// Shared code of the HTML overlays: messages from C#, one clock, one drawing loop, frame counting, edit mode.
// C# computes everything; a page only draws what it gets.
const SL = (() => {
    const handlers = {};
    const drawers = [];
    const stats = { frames: 0, gap: 0, last: 0, since: 0 };
    let editName = '';
    let cap = 0;            // most frames per second to draw (0 = every frame the browser offers)
    let lastDrawn = 0;

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

    function setEdit(on) {
        document.body.classList.toggle('sl-edit', on);
    }

    if (window.chrome && chrome.webview) {
        chrome.webview.addEventListener('message', (event) => {
            const message = event.data;
            if (message && message.type === 'edit') {
                setEdit(message.on);
            } else if (message && message.type === 'cap') {
                cap = message.fps || 0;
            } else if (message && handlers[message.type]) {
                handlers[message.type](message);
            }
        });
    }

    // Fallback for edit mode: if the CSS drag area does not reach the window, ask C# to start the move.
    window.addEventListener('pointerdown', () => {
        if (document.body.classList.contains('sl-edit')) {
            send({ type: 'drag' });
        }
    });

    function setName(name) {
        editName = name;
        const label = document.querySelector('.sl-edit-label');
        if (label) {
            label.textContent = name;
        }
    }

    window.addEventListener('DOMContentLoaded', () => {
        const frameBox = document.createElement('div');
        frameBox.className = 'sl-edit-frame';
        const label = document.createElement('div');
        label.className = 'sl-edit-label';
        label.textContent = editName;
        document.body.append(frameBox, label);
        requestAnimationFrame(frame);
    });

    return { now, on, draw, send, setName };
})();
