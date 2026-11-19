'use strict';
// Standings and relatives: builds the rows once per layout and fills them per frame. All values, colours and sizes come
// from C#; this file only places and animates them. Animations run in the shared drawing loop (SL.draw), so the overlay
// frame rate of General holds.
(() => {
    const MUTED = '#8e98a6';
    const TEXT = '#e8ecf1';
    const UP = 'M 0.5,5.5 L 4,2 L 7.5,5.5';
    const DOWN = 'M 0.5,2 L 4,5.5 L 7.5,2';
    const MOON = 'M 6.5,1.2 A 4.8,4.8 0 1 0 10.8,8.2 A 3.8,3.8 0 0 1 6.5,1.2 Z M 9.2,1.6 L 9.2,3.8 M 8.1,2.7 L 10.3,2.7';
    const SUN = 'M 0.8,9.5 L 11.2,9.5 M 3,9.5 A 3,3 0 0 1 9,9.5 M 6,2 L 6,3.6 M 1.6,4.4 L 2.8,5.5 M 10.4,4.4 L 9.2,5.5';
    const panel = document.getElementById('panel');
    let layout = null;
    let slots = [];
    let header = {};
    let footer = {};
    let lastSlotOf = new Map();       // car id -> slot of the last frame
    const motions = [];               // running animations

    const el = (tag, className, parent) => {
        const e = document.createElement(tag);
        if (className) {
            e.className = className;
        }
        if (parent) {
            parent.appendChild(e);
        }
        return e;
    };

    const svgPath = (className, parent) => {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('class', className);
        const size = className === 'chev' ? 8 : 12;
        svg.setAttribute('viewBox', '0 0 ' + size + ' ' + size);
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        svg.appendChild(path);
        parent.appendChild(svg);
        return { svg, path };
    };

    // cheap setters: touch the DOM only when something changed
    const setText = (e, v) => { v = v == null ? '' : String(v); if (e.textContent !== v) { e.textContent = v; } };
    const setStyle = (e, k, v) => { v = v == null ? '' : v; if (e.style[k] !== v) { e.style[k] = v; } };
    const setShown = (e, on) => setStyle(e, 'display', on ? '' : 'none');
    const setPath = (p, d) => { d = d || ''; if (p.getAttribute('d') !== d) { p.setAttribute('d', d); } };
    const fontOf = (name) => name ? '"' + name + '", "Segoe UI", sans-serif' : '';

    // ------------------------------------------------------------------ layout

    SL.on('layout', (l) => {
        layout = l;
        lastSlotOf = new Map();
        motions.length = 0;
        const r = l.round;
        panel.innerHTML = '';
        setStyle(panel, 'width', l.width + 'px');
        setStyle(panel, 'transform', 'scale(' + l.scale + ')');
        setStyle(panel, 'background', l.card);
        setStyle(panel, 'borderRadius', 8 * r + 'px');
        setStyle(panel, 'fontFamily', fontOf(l.font));
        setStyle(panel, 'fontWeight', String(l.weight));
        const inner = l.width - 16;

        header = {};
        if (l.header) {
            const box = el('div', 'hdr', panel);
            box.style.width = inner + 'px';
            box.style.background = l.hf;
            box.style.borderRadius = 5 * r + 'px';
            const left = el('div', 'side', box);
            const mid = el('div', 'mid', box);
            const right = el('div', 'side', box);
            for (const item of l.header) {
                const parent = item.slot === 'H.Left' ? left : item.slot === 'H.Right' ? right : mid;
                const e = el('div', 'hitem', parent);
                e.style.margin = item.slot === 'H.Left' ? '0 0 0 10px' : item.slot === 'H.Right' ? '0 10px 0 0' : '0 8px';
                let icon = null;
                if (item.icon) {
                    icon = svgPath('', e);
                    setPath(icon.path, item.icon);
                }
                const words = el('span', '', e);
                if (item.label) {
                    el('span', 'lbl', words).textContent = item.label;
                }
                const val = el('span', 'val', words);
                header[item.slot] = { e, val, icon, kind: item.kind };
            }
        }

        if (l.labels) {
            const row = el('div', 'labels', panel);
            for (const c of l.columns) {
                const d = el('div', '', row);
                d.style.width = c.w + 'px';
                d.style.textAlign = c.align === 'r' ? 'right' : c.align === 'c' ? 'center' : 'left';
                d.textContent = c.label;
            }
        }

        const pitch = l.rowHeight + l.gap;
        const area = el('div', '', panel);
        area.id = 'rows';
        area.style.height = l.rows * pitch + 'px';
        area.style.width = inner + 'px';
        slots = [];
        for (let s = 0; s < l.rows; s++) {
            slots.push(buildSlot(area, s, pitch));
        }

        footer = {};
        if (l.footer) {
            const box = el('div', 'ftr', panel);
            box.style.width = inner + 'px';
            box.style.background = l.hf;
            box.style.borderRadius = 5 * r + 'px';
            l.footer.forEach((name, i) => {
                const e = el('div', 'fitem', box);
                e.style.justifyContent = i === 0 ? 'flex-start' : i === l.footer.length - 1 ? 'flex-end' : 'center';
                let arrow = null;
                if (name === 'Delta') {
                    arrow = svgPath('chev', e);
                } else {
                    el('span', 'lbl', e).textContent = name.toUpperCase();
                }
                footer[name] = { val: el('span', 'val', e), arrow };
            });
        }
    });

    function buildSlot(area, s, pitch) {
        const l = layout;
        const slot = { el: el('div', 'slot', area), cells: [] };
        slot.el.style.top = s * pitch + 'px';
        slot.el.style.height = pitch + 'px';
        slot.bg = el('div', 'rowbg', slot.el);
        slot.bg.style.borderRadius = (l.gap > 0 ? 4 : 3) * l.round + 'px';
        // the class tab: "Line" lies under the cells, the others on top
        slot.tab = el('div', 'tab');
        slot.tab.style.height = 24 - 4 + 'px';
        if (l.tabStyle === 'Line') {
            slot.tab.style.left = '0';
            slot.tab.style.right = '0';
            slot.tab.style.borderRadius = 3 * l.round + 'px ' + 3 * l.round + 'px 0 0';
            slot.el.appendChild(slot.tab);
        }
        const row = el('div', 'cells', slot.el);
        for (const c of l.columns) {
            slot.cells.push(buildCell(row, c));
        }
        if (l.tabStyle !== 'Line') {
            slot.tab.style.left = '2px';
            slot.tab.style.borderRadius = 4 * l.round + 'px ' + 4 * l.round + 'px 0 0';
            slot.tab.style.zIndex = '5';
            slot.el.appendChild(slot.tab);
        }
        setShown(slot.tab, false);
        return slot;
    }

    function buildCell(row, c) {
        const l = layout;
        const cell = { key: c.key, outer: el('div', 'cell ' + c.align, row) };
        cell.outer.dataset.key = c.key;
        cell.outer.style.width = c.w + 'px';
        if (c.font) {
            cell.outer.style.fontFamily = fontOf(c.font);
        }
        cell.outer.style.fontWeight = String(c.weight);
        cell.box = el('div', 'box', cell.outer);
        cell.box.style.borderRadius = 3 * l.round + 'px';
        switch (c.key) {
            case 'Class':
                cell.outer.classList.add('cls');
                cell.box.style.borderRadius = 2 * l.round + 'px';
                break;
            case 'Gain':
                cell.arrow = svgPath('chev', cell.box);
                cell.text = el('span', 'txt', cell.box);
                break;
            case 'Tyre':
                cell.pill = el('div', 'tyre', cell.box);
                cell.pill.style.borderRadius = 9 * l.round + 'px';
                cell.pill.style.background = c.fill || '';
                cell.ring = el('div', 'ring', cell.pill);
                cell.text = el('span', 'txt', cell.pill);
                cell.text.style.color = MUTED;
                cell.text.style.marginRight = '4px';
                break;
            case 'iR':
                cell.pill = el('div', 'pill', cell.box);
                cell.pill.style.background = c.fill || '';
                cell.pill.style.borderRadius = 4 * l.round + 'px';
                cell.pill.style.padding = '0 3px';
                if (l.showGain) {
                    cell.pill.style.width = '100%';
                    cell.pill.style.justifyContent = 'center';
                }
                cell.text = el('span', 'txt', cell.pill);
                cell.text.style.minWidth = l.ratingWidth + 'px';
                cell.text.style.textAlign = 'center';
                if (l.showGain) {
                    const holder = el('span', '', cell.pill);
                    holder.style.width = '12px';
                    holder.style.display = 'flex';
                    holder.style.justifyContent = 'center';
                    cell.arrow = svgPath('chev', holder);
                    cell.arrow.svg.style.marginRight = '0';
                    cell.change = el('span', 'txt', cell.pill);
                    cell.change.style.minWidth = l.changeWidth + 'px';
                    cell.change.style.textAlign = 'center';
                }
                break;
            case 'Pit':
                cell.box.style.justifyContent = 'space-between';
                cell.text = el('span', 'txt', cell.box);
                cell.detail = el('span', 'txt', cell.box);
                break;
            case 'Flag':
                cell.img = el('img', 'flag', cell.box);
                cell.box.style.padding = '0';
                break;
            case 'Delta':
                cell.deltas = [];
                break;
            case 'Lic':
                cell.text = el('span', 'txt', cell.box);
                cell.lic = el('div', 'lic', cell.box);
                cell.box.style.padding = '0';
                cell.lic.style.borderRadius = 4 * l.round + 'px';
                cell.num = el('span', 'num', cell.lic);
                cell.let = el('span', 'let', cell.lic);
                setShown(cell.num, l.licNumber);
                break;
            case 'Num':
                cell.text = el('span', 'txt', cell.box);
                cell.text.style.fontWeight = l.numStyle % 2 === 1 ? '700' : '';
                cell.text.style.fontStyle = l.numStyle % 4 >= 2 ? 'italic' : '';
                break;
            default:
                cell.text = el('span', 'txt', cell.box);
                if (c.key === 'Name' && l.smallCaps) {
                    cell.text.style.fontVariant = 'small-caps';
                }
        }
        return cell;
    }

    // ------------------------------------------------------------------ frames

    SL.on('frame', (f) => {
        if (!layout) {
            return;
        }
        const now = SL.now();
        const pitch = layout.rowHeight + layout.gap;
        const nextSlotOf = new Map();
        f.rows.forEach((row, s) => {
            const slot = slots[s];
            if (!slot) {
                return;
            }
            setStyle(slot.bg, 'background', row.bg);
            fillTab(slot, row.tab, f.cardOpacity);
            row.cells.forEach((data, c) => fillCell(slot.cells[c], data));
            if (row.car) {
                nextSlotOf.set(row.id, s);
                const before = lastSlotOf.get(row.id);
                if (f.anim > 0 && before !== undefined && before !== s) {
                    startMotion(slot, (before - s) * pitch, f.anim, now);
                }
            }
        });
        lastSlotOf = nextSlotOf;

        if (f.header) {
            for (const name in header) {
                const h = header[name];
                const v = f.header[name];
                setShown(h.e, v != null);
                setText(h.val, v);
                if (h.kind === 'Sim' && h.icon) {
                    setPath(h.icon.path, f.day ? SUN : MOON);
                }
            }
        }

        if (f.footer) {
            for (const name in footer) {
                const ft = footer[name];
                setText(ft.val, f.footer[name]);
                setStyle(ft.val, 'color', f.footer[name + 'Colour'] || TEXT);
                if (ft.arrow) {
                    setPath(ft.arrow.path, f.footer.DeltaDir > 0 ? UP : f.footer.DeltaDir < 0 ? DOWN : '');
                    setStyle(ft.arrow.svg, 'stroke', f.footer.DeltaColour);
                }
            }
        }
    });

    function fillTab(slot, tab, card) {
        setShown(slot.tab, !!tab);
        if (!tab) {
            return;
        }
        setText(slot.tab, tab.t);
        const style = layout.tabStyle;
        if (style === 'Line') {
            const alpha = card >= 0.9 ? 0 : 1 - 0.1 / (1 - card);
            setStyle(slot.tab, 'background', alpha > 0 ? 'rgba(14,18,25,' + alpha.toFixed(3) + ')' : '');
            setStyle(slot.tab, 'borderLeft', '2px solid ' + tab.c);
            setStyle(slot.tab, 'borderBottom', '2px solid ' + tab.c);
            setStyle(slot.tab, 'color', tab.c);
        } else if (style === 'Subtle' && card >= 0.9) {
            setStyle(slot.tab, 'background', withAlpha(tab.c, 0.333));
            setStyle(slot.tab, 'borderLeft', '3px solid ' + tab.c);
            setStyle(slot.tab, 'color', TEXT);
        } else {
            setStyle(slot.tab, 'background', tab.c);
            setStyle(slot.tab, 'borderLeft', '');
            setStyle(slot.tab, 'color', tab.dark ? '#101216' : '#ffffff');
        }
    }

    const withAlpha = (rgba, a) => rgba.replace(/,[^,]*\)$/, ',' + a + ')');

    function fillCell(cell, d) {
        setStyle(cell.box, 'background', d.b);
        switch (cell.key) {
            case 'Class':
                return;
            case 'Gain':
                setText(cell.text, d.t);
                setStyle(cell.text, 'color', d.f);
                setPath(cell.arrow.path, d.g > 0 ? UP : d.g < 0 ? DOWN : '');
                setStyle(cell.arrow.svg, 'stroke', d.m);
                return;
            case 'Tyre':
                setShown(cell.pill, !!d.tc);
                setText(cell.ring, d.tc);
                setStyle(cell.ring, 'borderColor', d.tk);
                setText(cell.text, d.t);
                return;
            case 'iR':
                setShown(cell.pill, d.hr);
                setText(cell.text, d.t);
                setStyle(cell.text, 'color', d.f);
                if (cell.change) {
                    setText(cell.change, d.rc ? Math.abs(d.rc) : '');
                    setStyle(cell.change, 'color', d.rk);
                    setPath(cell.arrow.path, d.rc > 0 ? UP : d.rc < 0 ? DOWN : '');
                    setStyle(cell.arrow.svg, 'stroke', d.rk);
                }
                return;
            case 'Pit':
                setText(cell.text, d.t);
                setStyle(cell.text, 'color', d.f);
                if (d.d !== undefined) {
                    setText(cell.detail, d.d);
                    setStyle(cell.detail, 'color', d.m);
                }
                return;
            case 'Flag': {
                const src = d.fl ? 'flags/' + d.fl + '.png' : '';
                if (cell.img.getAttribute('src') !== src) {
                    if (src) {
                        cell.img.setAttribute('src', src);
                    } else {
                        cell.img.removeAttribute('src');
                    }
                }
                setStyle(cell.img, 'visibility', src ? 'visible' : 'hidden');
                return;
            }
            case 'Delta': {
                const count = d.dl ? d.dl.length : 0;
                while (cell.deltas.length < count) {
                    const s = el('span', 'txt', cell.box);
                    s.style.width = layout.deltaWidth + 'px';
                    s.style.textAlign = 'center';
                    s.style.flex = 'none';
                    cell.deltas.push(s);
                }
                cell.deltas.forEach((s, k) => {
                    setText(s, k < count ? d.dl[k] : '');
                    setStyle(s, 'color', k < count ? d.dk[k] : '');
                });
                return;
            }
            case 'Lic':
                if (d.ls !== undefined && (d.t === '' || d.t == null)) {
                    // split licence: class letter on its colour, the rating on a darker shade
                    setShown(cell.text, false);
                    setShown(cell.lic, !!d.ls);
                    setShown(cell.box, true);
                    setText(cell.let, d.ll);
                    setStyle(cell.let, 'background', d.ls);
                    setStyle(cell.let, 'color', d.lt);
                    setText(cell.num, d.ln);
                    setStyle(cell.num, 'background', layout.licNumber ? d.lh : '');
                } else {
                    setShown(cell.lic, false);
                    setShown(cell.text, true);
                    setText(cell.text, d.t);
                    setStyle(cell.text, 'color', d.f);
                }
                return;
            default:
                setText(cell.text, d.t);
                setStyle(cell.text, 'color', d.f);
        }
    }

    // ------------------------------------------------------------------ preview on the settings page

    if (new URLSearchParams(location.search).get('preview') === '1') {
        document.body.classList.add('preview');
        window.addEventListener('click', (event) => {
            const cell = event.target.closest('.cell');
            if (cell && cell.dataset.key) {
                SL.send({ type: 'column', key: cell.dataset.key });
            }
        });
    }

    // ------------------------------------------------------------------ animation (Slide, Pop, Fade)

    function startMotion(slot, from, seconds, now) {
        const style = layout.animStyle;
        const duration = style === 'Pop' ? seconds * 1.3 : style === 'Fade' ? seconds * 4 : seconds;
        for (let i = motions.length - 1; i >= 0; i--) {
            if (motions[i].slot === slot) {
                motions.splice(i, 1);
            }
        }
        motions.push({ slot, from, style, start: now, duration });
    }

    const quadOut = (x) => 1 - (1 - x) * (1 - x);
    const backOut = (x) => { const s = 0.8 * 1.70158; x -= 1; return x * x * ((s + 1) * x + s) + 1; };
    const sineInOut = (x) => -(Math.cos(Math.PI * x) - 1) / 2;

    SL.draw((now) => {
        for (let i = motions.length - 1; i >= 0; i--) {
            const m = motions[i];
            const x = Math.min(1, (now - m.start) / m.duration);
            const e = m.slot.el;
            if (m.style === 'Pop') {
                e.style.transform = x < 1 ? 'scale(' + (0.82 + 0.18 * backOut(x)) + ')' : '';
            } else if (m.style === 'Fade') {
                e.style.opacity = x < 1 ? String(sineInOut(x)) : '';
            } else {
                e.style.transform = x < 1 ? 'translateY(' + m.from * (1 - quadOut(x)) + 'px)' : '';
            }
            if (x >= 1) {
                motions.splice(i, 1);
            }
        }
    });
})();
