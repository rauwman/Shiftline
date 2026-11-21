'use strict';
// The Battle Bar: builds the header, the cards and the footer once per layout and fills them per frame. All values, colours
// and sizes come from C# (BattleBarOverlay.Html.cs); this file only places and animates them, in the shared drawing loop
// (SL.draw), so the overlay frame rate of General holds.
(() => {
    const MUTED = '#8e98a6';
    const TEXT = '#e8ecf1';
    const DARK = '#101216';
    const GREEN = 'rgba(94,224,122,1)';
    const LOSS = 'rgba(240,90,74,1)';
    const UP = 'M 0.5,5.5 L 4,2 L 7.5,5.5';
    const DOWN = 'M 0.5,2 L 4,5.5 L 7.5,2';
    const MOON = 'M 6.5,1.2 A 4.8,4.8 0 1 0 10.8,8.2 A 3.8,3.8 0 0 1 6.5,1.2 Z M 9.2,1.6 L 9.2,3.8 M 8.1,2.7 L 10.3,2.7';
    const SUN = 'M 0.8,9.5 L 11.2,9.5 M 3,9.5 A 3,3 0 0 1 9,9.5 M 6,2 L 6,3.6 M 1.6,4.4 L 2.8,5.5 M 10.4,4.4 L 9.2,5.5';
    const root = document.getElementById('bb');
    let layout = null;
    let header = {};
    let headBox = null;
    let footer = {};
    let sides = {};
    let strip = null;
    let run = null;              // running ticker: the moving row
    let slots = [];              // cards and fixed ticker
    let entries = [];            // running ticker
    let lastSlotOf = new Map();  // car id -> slot of the last frame
    let lastCarIn = [];          // slot -> car id of the last frame
    let lastPlayerPos = 0;
    const motions = [];
    const flows = [];            // frames with a turning gradient
    const scroll = { offset: 0, last: 0, pauseUntil: 0, paused: false, player: -1, half: 0 };

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

    const svgPath = (className, size, parent) => {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('class', className);
        svg.setAttribute('viewBox', '0 0 ' + size + ' ' + size);
        const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        svg.appendChild(path);
        parent.appendChild(svg);
        return { svg, path };
    };

    const setText = (e, v) => { v = v == null ? '' : String(v); if (e.textContent !== v) { e.textContent = v; } };
    const setStyle = (e, k, v) => { v = v == null ? '' : v; if (e.style[k] !== v) { e.style[k] = v; } };
    const setShown = (e, on) => setStyle(e, 'visibility', on ? '' : 'hidden');
    const setPath = (p, d) => { d = d || ''; if (p.getAttribute('d') !== d) { p.setAttribute('d', d); } };
    const fontOf = (name) => name ? '"' + name + '", "Segoe UI", sans-serif' : '';

    // The tyre letter: the outline of the glyph from C# (LetterSvg). The SVG fills the inside of the ring and its view is
    // centred on the middle of the outline, so the letter sits in the middle by its ink (not by the line height of a font)
    // and no separate small box is rounded to whole pixels.
    const setLetter = (holder, l) => {
        if (!holder.letter) {
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('class', 'letter');
            const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            svg.appendChild(path);
            holder.textContent = '';
            holder.appendChild(svg);
            holder.letter = { svg, path, key: null };
        }
        const key = l ? l.p : '';
        const t = holder.letter;
        if (t.key === key) {
            return;
        }
        t.key = key;
        t.svg.style.display = l ? '' : 'none';
        if (l) {
            const size = holder.clientWidth || 10;
            const cx = l.x + l.w / 2;
            const cy = l.y + l.h / 2;
            t.svg.setAttribute('viewBox', (cx - size / 2) + ' ' + (cy - size / 2) + ' ' + size + ' ' + size);
            t.svg.setAttribute('width', size);
            t.svg.setAttribute('height', size);
            t.path.setAttribute('d', l.p);
            t.path.setAttribute('fill-rule', l.evenodd ? 'evenodd' : 'nonzero');
        }
    };

    // ------------------------------------------------------------------ layout

    SL.on('battle-layout', (l) => {
        layout = l;
        document.body.dataset.kind = 'battle';
        lastSlotOf = new Map();
        lastCarIn = [];
        lastPlayerPos = 0;
        motions.length = 0;
        flows.length = 0;
        const r = l.round;
        root.innerHTML = '';
        setStyle(root, 'width', l.width + 'px');
        setStyle(root, 'height', l.height + 'px');
        setStyle(root, 'transform', 'scale(' + l.scale + ')');
        setStyle(root, 'background', l.panel);
        setStyle(root, 'borderRadius', 8 * r + 'px');
        setStyle(root, 'fontFamily', fontOf(l.font));
        setStyle(root, 'fontWeight', String(l.weight));

        header = {};
        headBox = null;
        if (l.header) {
            headBox = el('div', 'bb-pill bb-head', root);
            headBox.style.background = l.hf;
            headBox.style.borderRadius = 4 * r + 'px';
            for (const item of l.header) {
                const e = el('div', 'hitem', headBox);
                let icon = null;
                if (item.icon) {
                    icon = svgPath('', 12, e);
                    if (item.kind !== 'Sim') {
                        setPath(icon.path, item.icon);
                    }
                }
                const words = el('span', 'val', e);
                if (item.max) {
                    words.style.maxWidth = item.max + 'px';
                }
                if (item.label) {
                    el('span', 'lbl', words).textContent = item.label + '  ';
                }
                const val = el('span', '', words);
                header[item.slot] = { e, val, icon, kind: item.kind };
            }
        }

        const mid = el('div', 'bb-mid', root);
        mid.style.height = l.rowH + 'px';
        sides = {};
        if (l.left) {
            sides.left = buildSide(mid, l.left, '0 6px 0 0');
        }
        strip = el('div', 'bb-strip' + (l.mode === 'Scroll' ? ' scroll' : ''), mid);
        strip.style.width = l.stripW + 'px';
        strip.style.height = (l.mode === 'Cards' ? l.rowH : l.cardH) + 'px';
        if (l.rightSide) {
            sides.right = buildSide(mid, l.rightSide, '0 0 0 6px');
        }

        slots = [];
        entries = [];
        run = null;
        if (l.mode === 'Scroll') {
            run = el('div', 'bb-run', strip);
            scroll.offset = 0;
            scroll.paused = false;
            scroll.pauseUntil = 0;
            scroll.half = 0;
        } else {
            l.xs.forEach((x, i) => {
                const zoom = i === l.playerSlot ? l.zoom : 1;
                const slot = buildCard(strip, zoom);
                slot.x = x;
                slot.mover.style.left = x + 'px';
                slot.mover.style.top = ((l.mode === 'Cards' ? l.rowH : l.cardH) - l.cardH * zoom) / 2 + 'px';
                slots.push(slot);
            });
        }

        footer = {};
        if (l.footer) {
            const box = el('div', 'bb-pill bb-foot', root);
            box.style.background = l.hf;
            box.style.borderRadius = 4 * r + 'px';
            for (const name of l.footer) {
                el('span', 'lbl', box).textContent = name.toUpperCase();
                footer[name] = el('span', 'val', box);
            }
        }
    });

    function buildSide(parent, side, margin) {
        const l = layout;
        const box = el('div', 'bb-side' + (l.rowH > 30 ? '' : ' low'), parent);
        box.style.height = l.rowH + 'px';
        box.style.margin = margin;
        box.style.background = l.card;
        box.style.borderRadius = 5 * l.round + 'px';
        el('span', 'lbl', box).textContent = side.label;
        return { val: el('span', 'val', box) };
    }

    // One card, as the WPF bar lays it out:
    //   [position][class bar] | flag name ...... make   gap |
    //                         | iRating licence   pit tyre gained |
    function buildCard(parent, zoom) {
        const l = layout;
        const slot = { zoom };
        slot.mover = el('div', 'bb-mover', parent);
        slot.zoomer = el('div', 'bb-zoom', slot.mover);
        if (zoom !== 1) {
            slot.zoomer.style.transform = 'scale(' + zoom + ')';
        }
        slot.card = el('div', 'bb-card' + (l.twoLines ? ' bb-two' : ''), slot.zoomer);
        fillShell(slot, slot.card);
        return slot;
    }

    function fillShell(slot, card) {
        const l = layout;
        card.style.width = l.cardW + 'px';
        card.style.height = l.cardH + 'px';
        card.style.borderRadius = 5 * l.round + 'px';
        card.style.background = l.card;
        slot.flash = el('div', 'bb-flash', card);
        const inner = el('div', 'bb-in' + (l.twoLines ? ' two' : ''), card);
        slot.frame = el('div', 'bb-frame', card);
        if (l.pos) {
            slot.pos = el('div', 'bb-pos', inner);
            slot.pos.style.background = l.posFill;
            slot.pos.style.borderRadius = 3 * l.round + 'px';
            slot.pos.style.color = l.posDark ? DARK : TEXT;
            slot.pos.style.gridColumn = '1';
        }
        if (l.cls) {
            slot.cls = el('div', 'bb-cls', inner);
            slot.cls.style.height = l.cardH - 18 + 'px';
            slot.cls.style.borderRadius = 1.5 * l.round + 'px';
            slot.cls.style.background = MUTED;
            slot.cls.style.gridColumn = '2';
        }
        if (l.name || l.flag) {
            const box = el('div', 'bb-namebox', inner);
            box.style.gridColumn = '3';
            box.style.gridRow = '1';
            if (l.flag) {
                slot.flag = el('img', 'bb-flag', box);
                slot.flag.style.borderRadius = 2 * l.round + 'px';
                slot.flag.style.visibility = 'hidden';
            }
            if (l.name) {
                slot.name = el('span', 'bb-name', box);
                slot.laps = el('span', 'bb-laps', box);
                slot.laps.style.display = 'none';
            }
        }
        if (l.make) {
            slot.make = el('div', 'bb-make', inner);
            slot.make.style.gridColumn = '4';
            slot.make.style.gridRow = '1';
        }
        if (l.gapOn) {
            const box = el('div', 'bb-gap', inner);
            box.style.width = l.gapW + 'px';
            box.style.gridColumn = '5';
            box.style.gridRow = '1';
            slot.trend = svgPath('bb-chev', 8, box);
            slot.gap = el('span', '', box);
        }
        if (l.twoLines) {
            if (l.ir || l.lic) {
                const rate = el('div', 'bb-row2 bb-rate', inner);
                if (l.ir) {
                    slot.ir = el('span', 'bb-ir', rate);
                }
                if (l.lic) {
                    slot.lic = el('div', 'bb-lic', rate);
                    slot.lic.style.borderRadius = 3 * l.round + 'px';
                    slot.licNum = el('span', 'num', slot.lic);
                    slot.licLet = el('span', 'let', slot.lic);
                    slot.licLet.style.borderRadius = l.licNumber ? '0 ' + 3 * l.round + 'px ' + 3 * l.round + 'px 0' : 3 * l.round + 'px';
                    if (!l.licNumber) {
                        slot.licNum.style.display = 'none';
                    }
                }
            }
            if (l.right.length > 0) {
                const right = el('div', 'bb-row2 bb-right', inner);
                for (const key of l.right) {
                    if (key === 'Pit') {
                        slot.pit = el('span', 'bb-pit', right);
                    } else if (key === 'Tyre') {
                        slot.tyre = el('span', 'bb-tyre', right);
                    } else if (key === 'Gain') {
                        const gain = el('span', 'bb-gain', right);
                        slot.gainArrow = svgPath('bb-chev', 8, gain);
                        slot.gain = el('span', '', gain);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ frames

    SL.on('battle-frame', (f) => {
        if (!layout) {
            return;
        }
        const now = SL.now();

        if (f.header && headBox) {
            let any = false;
            for (const name in header) {
                const h = header[name];
                const v = f.header[name];
                h.e.style.display = v == null ? 'none' : '';
                any = any || v != null;
                setText(h.val, v);
                if (h.kind === 'Sim' && h.icon) {
                    setPath(h.icon.path, f.day ? SUN : MOON);
                }
            }
            setShown(headBox, any);
        }

        for (const name in sides) {
            const s = f.sides[name];
            if (s) {
                setText(sides[name].val, s.v);
                setStyle(sides[name].val, 'color', s.c);
            }
        }

        if (f.footer) {
            for (const name in footer) {
                setText(footer[name], f.footer[name]);
                setStyle(footer[name], 'color', name === 'Last' ? TEXT : f.footer[name + 'Colour']);
            }
        }

        if (layout.mode === 'Scroll') {
            fillScroll(f);
            return;
        }

        const style = layout.style;
        const nextSlotOf = new Map();
        f.cards.forEach((card, i) => {
            const slot = slots[i];
            if (!slot) {
                return;
            }
            setShown(slot.mover, !!card);
            if (!card) {
                lastCarIn[i] = undefined;
                return;
            }
            nextSlotOf.set(card.id, i);
            const changed = lastCarIn[i] !== undefined && lastCarIn[i] !== card.id;
            if (f.anim > 0 && changed && (style === 'Fade' || style === 'Pop')) {
                // another car in this card: a copy of the old card fades out above the new one, so nothing jumps
                crossFade(slot, style === 'Pop' ? f.fade * 0.5 : f.fade, now);
                motions.push({ el: slot.card, kind: style === 'Pop' ? 'pop' : 'fadein', start: now, duration: f.fade });
            }
            fillCard(slot, card);
            const before = lastSlotOf.get(card.id);
            if (f.anim > 0 && style === 'Slide' && before !== undefined && before !== i && before < slots.length) {
                startSlide(slot, slots[before].x - slot.x, f.anim, now);
            }
            lastCarIn[i] = card.id;
        });
        lastSlotOf = nextSlotOf;

        // the player's card flashes green or red when the position changes
        if (lastPlayerPos > 0 && f.playerPos > 0 && f.playerPos !== lastPlayerPos && layout.flash) {
            const index = layout.playerSlot >= 0 ? layout.playerSlot : f.cards.findIndex((c) => c && c.frame === 'Player');
            if (index >= 0 && slots[index]) {
                const flash = slots[index].flash;
                flash.style.background = f.playerPos < lastPlayerPos ? GREEN : LOSS;
                motions.push({ el: flash, kind: 'flash', start: now, duration: 1.4 });
            }
        }
        lastPlayerPos = f.playerPos;
    });

    function fillCard(slot, d) {
        const l = layout;
        setStyle(slot.card, 'background', d.bg);
        if (slot.pos) {
            setText(slot.pos, d.pos);
        }
        if (slot.cls) {
            setStyle(slot.cls, 'background', d.cls);
        }
        if (slot.name) {
            setText(slot.name, d.name);
            setStyle(slot.name, 'color', d.nameC);
            setText(slot.laps, d.laps);
            setStyle(slot.laps, 'color', d.nameC);
            setStyle(slot.laps, 'display', d.laps ? '' : 'none');
        }
        if (slot.flag) {
            const src = d.flag ? 'flags/' + d.flag + '.png' : '';
            if (slot.flag.getAttribute('src') !== src) {
                if (src) {
                    slot.flag.setAttribute('src', src);
                } else {
                    slot.flag.removeAttribute('src');
                }
            }
            setShown(slot.flag, !!src);
        }
        if (slot.make) {
            setText(slot.make, d.make);
        }
        if (slot.gap) {
            setText(slot.gap, d.gap);
            setPath(slot.trend.path, d.trend > 0 ? UP : d.trend < 0 ? DOWN : '');
            setStyle(slot.trend.svg, 'stroke', d.trend > 0 ? GREEN : d.trend < 0 ? LOSS : MUTED);
        }
        if (slot.ir) {
            setText(slot.ir, d.ir);
        }
        if (slot.lic) {
            setShown(slot.lic, !!d.lic);
            if (d.lic) {
                setText(slot.licLet, d.lic.l);
                setText(slot.licNum, d.lic.n);
                setStyle(slot.licLet, 'background', d.lic.s);
                setStyle(slot.licLet, 'color', d.lic.t);
                setStyle(slot.lic, 'background', l.licNumber ? d.lic.h : '');
            }
        }
        if (slot.pit) {
            setText(slot.pit, d.pit);
        }
        if (slot.gain) {
            const g = d.gain || 0;
            setText(slot.gain, g !== 0 ? Math.abs(g) : '');
            setStyle(slot.gain, 'color', g > 0 ? GREEN : g < 0 ? LOSS : MUTED);
            setPath(slot.gainArrow.path, g > 0 ? UP : g < 0 ? DOWN : '');
            setStyle(slot.gainArrow.svg, 'stroke', g > 0 ? GREEN : LOSS);
        }
        if (slot.tyre) {
            setShown(slot.tyre, !!d.tyre);
            if (d.tyre) {
                setLetter(slot.tyre, d.tyre.l);
                setStyle(slot.tyre, 'borderColor', d.tyre.k);
            }
        }
        setFrame(slot, d.frame === 'Player' ? l.playerFrame : d.frame === 'Battle' ? { c: l.battle } : d.frame === 'Class' ? { c: d.cls } : null);
    }

    function setFrame(slot, frame) {
        const i = flows.indexOf(slot.frame);
        if (frame && frame.flow) {
            slot.frame.dataset.flow = frame.flow.join(',');
            if (i < 0) {
                flows.push(slot.frame);
            }
            return;
        }
        if (i >= 0) {
            flows.splice(i, 1);
        }
        setStyle(slot.frame, 'background', frame ? frame.c : '');
    }

    // ------------------------------------------------------------------ running ticker

    function fillScroll(f) {
        const order = f.order || [];
        const wanted = order.length * 2;
        if (entries.length !== wanted) {
            run.innerHTML = '';
            entries = [];
            for (let i = 0; i < wanted; i++) {
                const slot = { zoom: 1 };
                slot.card = el('div', 'bb-card', run);
                slot.card.style.marginRight = layout.gap + 'px';
                slot.card.style.flex = 'none';
                fillShell(slot, slot.card);
                slot.mover = slot.card;
                entries.push(slot);
            }
            scroll.offset = 0;
        }
        entries.forEach((slot, i) => fillCard(slot, order[i % order.length]));
        scroll.player = f.playerIndex;
        scroll.half = order.length * layout.pitch;
    }

    // ------------------------------------------------------------------ animation

    function startSlide(slot, from, seconds, now) {
        for (let i = motions.length - 1; i >= 0; i--) {
            if (motions[i].el === slot.mover && motions[i].kind === 'slide') {
                motions.splice(i, 1);
            }
        }
        motions.push({ el: slot.mover, kind: 'slide', from, start: now, duration: seconds });
    }

    function crossFade(slot, seconds, now) {
        if (slot.mover.style.visibility === 'hidden') {
            return;
        }
        const ghost = slot.card.cloneNode(true);
        ghost.style.position = 'absolute';
        ghost.style.left = '0';
        ghost.style.top = '0';
        ghost.style.transform = '';
        ghost.style.opacity = '1';
        ghost.style.pointerEvents = 'none';
        slot.zoomer.appendChild(ghost);
        motions.push({ el: ghost, kind: 'ghost', start: now, duration: seconds });
    }

    const quadInOut = (x) => x < 0.5 ? 2 * x * x : 1 - Math.pow(-2 * x + 2, 2) / 2;
    const quadOut = (x) => 1 - (1 - x) * (1 - x);
    const sineInOut = (x) => -(Math.cos(Math.PI * x) - 1) / 2;
    // WPF BackEase, EaseOut, amplitude 0.6
    const backOut = (x) => { const t = 1 - x; return 1 - (t * t * t - t * 0.6 * Math.sin(Math.PI * t)); };

    SL.draw((now) => {
        for (let i = motions.length - 1; i >= 0; i--) {
            const m = motions[i];
            const x = m.duration > 0 ? Math.min(1, Math.max(0, (now - m.start) / m.duration)) : 1;
            const e = m.el.style;
            switch (m.kind) {
                case 'slide':
                    e.transform = x < 1 ? 'translateX(' + m.from * (1 - quadInOut(x)) + 'px)' : '';
                    break;
                case 'fadein':
                    e.opacity = x < 1 ? String(sineInOut(x)) : '';
                    break;
                case 'pop': {
                    e.transform = x < 1 ? 'scale(' + (0.9 + 0.1 * backOut(x)) + ')' : '';
                    const y = Math.min(1, x / 0.6);
                    e.opacity = y < 1 ? String(quadOut(y)) : '';
                    break;
                }
                case 'ghost':
                    e.opacity = String(1 - sineInOut(x));
                    if (x >= 1) {
                        m.el.remove();
                    }
                    break;
                case 'flash':
                    e.opacity = x < 1 ? String(0.55 * (1 - quadOut(x))) : '0';
                    break;
            }
            if (x >= 1) {
                motions.splice(i, 1);
            }
        }

        // the turning frames: one turn in 4 seconds
        if (flows.length > 0) {
            const angle = ((now % 4) / 4 * 360 + 90).toFixed(1);
            for (const frame of flows) {
                const colours = frame.dataset.flow;
                frame.style.background = 'linear-gradient(' + angle + 'deg, ' + colours + ')';
            }
        }

        // the running ticker moves on every frame and stops for a moment when the player is in the middle
        if (layout && layout.mode === 'Scroll' && run && scroll.half > 0) {
            const dt = scroll.last > 0 ? Math.min(0.1, now - scroll.last) : 0;
            scroll.last = now;
            if (now >= scroll.pauseUntil) {
                const speed = layout.speed;
                scroll.offset += speed * dt;
                if (scroll.offset >= scroll.half) {
                    scroll.offset -= scroll.half;
                    scroll.paused = false;
                }
                if (!scroll.paused && scroll.player >= 0) {
                    let centre = scroll.player * layout.pitch + layout.cardW / 2 - layout.stripW / 2;
                    if (centre < 0) {
                        centre += scroll.half;
                    }
                    if (scroll.offset >= centre && scroll.offset - speed * dt < centre) {
                        scroll.offset = centre;
                        scroll.pauseUntil = now + 2.5;
                        scroll.paused = true;
                    }
                }
                run.style.transform = 'translateX(' + (-Math.round(scroll.offset * 10) / 10) + 'px)';
            }
        }
    });
})();
