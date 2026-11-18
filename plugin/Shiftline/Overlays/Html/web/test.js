'use strict';
// TEST ONLY: draws the made-up data of HtmlBench like the real overlays would: rows that slide to their new place (CSS
// transform, drawn by the compositor), a lap time that runs on every frame, and cars on a circle shown a little in the past
// between the two known places around that moment.
(() => {
    const query = new URLSearchParams(location.search);
    const kind = query.get('kind') || 'rows';
    const name = query.get('name') || 'Test';
    document.body.classList.add('kind-' + kind);
    document.getElementById('title').textContent = name;

    const rowHeight = 23;
    const rowsBox = document.getElementById('rows');
    const barBox = document.getElementById('bar');
    const clockText = document.getElementById('clock');
    const fpsText = document.getElementById('fps');
    const elements = new Map();
    let clock = null;

    function cell(row, className) {
        const span = document.createElement('span');
        span.className = className;
        row.appendChild(span);
        return span;
    }

    function setText(span, text) {
        if (span.textContent !== text) {
            span.textContent = text;
        }
    }

    // rows: one element per car (by id), moved with transform so the change of place animates
    SL.on('rows', (message) => {
        clock = message.clock;
        const seen = new Set();
        message.rows.forEach((data, index) => {
            seen.add(data.id);
            let item = elements.get(data.id);
            if (!item) {
                const box = document.createElement(kind === 'bar' ? 'div' : 'div');
                box.className = kind === 'bar' ? 'slot' : 'row';
                item = { box, cells: {} };
                if (kind === 'bar') {
                    item.cells.pos = document.createElement('b');
                    box.appendChild(item.cells.pos);
                    item.cells.name = cell(box, 'name');
                    item.cells.gap = cell(box, 'gap');
                    barBox.appendChild(box);
                } else {
                    item.cells.cls = cell(box, 'cls');
                    item.cells.pos = cell(box, 'pos');
                    item.cells.num = cell(box, 'num');
                    item.cells.name = cell(box, 'name');
                    item.cells.gap = cell(box, 'gap');
                    rowsBox.appendChild(box);
                }
                elements.set(data.id, item);
            }

            const where = kind === 'bar' ? `translateX(${8 + index * 110}px)` : `translateY(${index * rowHeight}px)`;
            if (item.where !== where) {
                item.where = where;
                item.box.style.transform = where;
            }
            item.box.classList.toggle('me', data.me);
            item.box.classList.toggle('odd', index % 2 === 1);
            setText(item.cells.pos, String(data.pos));
            setText(item.cells.name, data.name);
            setText(item.cells.gap, data.gap);
            if (item.cells.num) {
                setText(item.cells.num, data.num);
            }
            if (item.cells.cls && item.cls !== data.cls) {
                item.cls = data.cls;
                item.cells.cls.style.background = data.cls;
            }
        });

        for (const [id, item] of elements) {
            if (!seen.has(id)) {
                item.box.remove();
                elements.delete(id);
            }
        }
        rowsBox.style.height = (message.rows.length * rowHeight) + 'px';
    });

    // map: keep the last samples, draw about two updates in the past between the two samples around that moment
    const samples = [];
    SL.on('map', (message) => {
        samples.push(message);
        while (samples.length > 12) {
            samples.shift();
        }
    });

    const canvas = document.getElementById('map');
    const context = canvas.getContext('2d');

    function drawMap(t) {
        if (kind !== 'map' || samples.length < 2) {
            return;
        }
        const scale = window.devicePixelRatio || 1;
        const width = canvas.clientWidth * scale;
        const height = canvas.clientHeight * scale;
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
        const gap = (samples[samples.length - 1].t - samples[0].t) / (samples.length - 1);
        const at = t - Math.max(0.03, gap * 2);
        let a = samples[0];
        let b = samples[1];
        for (let i = 1; i < samples.length; i++) {
            a = samples[i - 1];
            b = samples[i];
            if (b.t >= at) {
                break;
            }
        }
        const f = Math.max(0, Math.min(1, (at - a.t) / Math.max(0.001, b.t - a.t)));
        const cx = width / 2;
        const cy = height / 2;
        const radius = Math.min(width, height) * 0.4;
        context.clearRect(0, 0, width, height);
        context.lineWidth = 10 * scale;
        context.strokeStyle = 'rgba(16, 20, 28, 0.8)';
        context.beginPath();
        context.arc(cx, cy, radius, 0, Math.PI * 2);
        context.stroke();
        context.font = `${10 * scale}px "Saira SemiCondensed"`;
        context.textAlign = 'center';
        context.textBaseline = 'middle';
        for (let i = a.cars.length - 1; i >= 0; i--) {
            let d = b.cars[i] - a.cars[i];
            d -= Math.round(d);
            const pct = a.cars[i] + d * f;
            const angle = pct * Math.PI * 2 - Math.PI / 2;
            const x = cx + Math.cos(angle) * radius;
            const y = cy + Math.sin(angle) * radius;
            context.fillStyle = i === 0 ? '#3fa9f5' : (i % 3 === 0 ? '#33ceff' : '#ffda59');
            context.beginPath();
            context.arc(x, y, 9 * scale, 0, Math.PI * 2);
            context.fill();
            context.fillStyle = '#000';
            context.fillText(String(i * 7 % 99 + 1), x, y);
        }
    }

    function drawClock(t) {
        if (!clock) {
            return;
        }
        const seconds = Math.max(0, clock.v + (t - clock.t));
        const minutes = Math.floor(seconds / 60);
        const rest = (seconds - minutes * 60).toFixed(3).padStart(6, '0');
        setText(clockText, minutes + ':' + rest);
    }

    let shown = 0;
    let frames = 0;
    let longest = 0;
    let last = 0;
    function drawFps(t) {
        frames++;
        if (last > 0) {
            longest = Math.max(longest, (t - last) * 1000);
        }
        last = t;
        if (t - shown >= 1) {
            fpsText.textContent = `${(frames / (t - shown)).toFixed(0)} fps, max ${longest.toFixed(0)} ms`;
            shown = t;
            frames = 0;
            longest = 0;
        }
    }

    SL.draw(drawMap);
    SL.draw(drawClock);
    SL.draw(drawFps);
})();
