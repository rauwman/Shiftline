'use strict';
// The Track Map on a canvas. C# sends the layout (sizes, colours, the line, the ring or the fitted track shape), the cars
// with the update rate (what each shows) and their places from the fast position feed. Like the WPF map, every car is
// shown a short delay in the past, between the two known places around that moment, so it moves evenly however unevenly
// the data arrives. Drawn in the shared drawing loop (SL.draw) with the overlay frame rate of General.
(() => {
    const canvas = document.getElementById('tm');
    const ctx = canvas.getContext('2d');
    const MAX_GLIDE = 0.05;           // a step longer than this part of a lap is a jump, not glided
    const KEPT = 8;                   // places kept per car
    const WAVE_SECONDS = 1.6;
    const WAVE_REACH = 0.45;
    const FLOW_SECONDS = 4;
    const SHIFTLINE = ['#01ABFF', '#903AA1', '#FF8C00', '#D60000'];
    const START = { White: '#F4F6F9', Black: '#0E1219', Grey: '#8E98A6' };
    const EDGE = 'rgba(14,18,25,0.902)';
    const PLAYER_RING = '#F4F6F9';
    const LAP_AHEAD = '#F0403E';
    const LAP_BEHIND = '#7FB2E0';
    const FASTEST = '#C07CFF';
    // A ring starts where the glass edge of a plain circle starts and reaches this far past the circle (dark edge included);
    // the player's circle is smaller by the same, so its ring ends at the player radius (as TrackMapOverlay.RingOutset).
    const RING_OUTSET = 2.5 + 1 - 1.5;
    const NEAR_PLAYER = 0.05;         // part of a lap around the player where the closest car lies on top
    let L = null;                     // layout
    let track = null;                 // the background and the track, drawn once per layout
    let cars = [];
    let player = -1;
    let offset = 0;
    let talking = -1;
    let sample = true;
    let frames = 0;                   // drawn with live data, for the data check of the settings page
    let statsSent = 0;
    let dpr = 1;
    let pitLen = null;                // distances along the pit lane
    const glides = new Map();
    const gaps = [];
    let lastUpdate = -1;
    let delay = 0.1;
    const figures = new Map();        // text|size -> measured ink box

    const wrap = (p) => p - Math.floor(p);
    const signed = (t, o) => { let d = (t - o) % 1; if (d > 0.5) { d -= 1; } else if (d <= -0.5) { d += 1; } return d; };

    // ------------------------------------------------------------------ messages

    SL.on('track-layout', (l) => {
        L = l;
        document.body.dataset.kind = 'track';
        dpr = window.devicePixelRatio || 1;
        canvas.style.width = l.width + 'px';
        canvas.style.height = l.height + 'px';
        canvas.width = Math.round(l.width * dpr);
        canvas.height = Math.round(l.height * dpr);
        figures.clear();
        pitLen = null;
        if (l.pit) {
            pitLen = [0];
            for (let i = 2; i < l.pit.length; i += 2) {
                pitLen.push(pitLen[pitLen.length - 1] + Math.hypot(l.pit[i] - l.pit[i - 2], l.pit[i + 1] - l.pit[i - 1]));
            }
        }
        track = drawTrack();
        if (document.fonts && l.font) {
            document.fonts.load('700 12px "' + l.font + '"').then(() => figures.clear());
        }
    });

    SL.on('track-cars', (m) => {
        cars = m.cars;
        player = m.player;
        offset = m.offset || 0;
        talking = m.talking;
        sample = m.sample;
    });

    SL.on('track-places', (m) => {
        stamp(m.t);
        for (const [idx, pct, pit] of m.cars) {
            addPlace(idx, pct, !!pit, m.t);
        }
    });

    // ------------------------------------------------------------------ gliding (as TrackMapOverlay: Stamp, AddPlace, Shown)

    function stamp(now) {
        if (lastUpdate >= 0) {
            gaps.push(Math.max(0.005, Math.min(0.5, now - lastUpdate)));
            if (gaps.length > 12) {
                gaps.shift();
            }
            const sorted = gaps.slice().sort((a, b) => a - b);
            delay = Math.max(0.04, Math.min(0.5, sorted[Math.floor(sorted.length / 2)] * 2 + 0.03));
        }
        lastUpdate = now;
    }

    function addPlace(idx, pct, pit, now) {
        let g = glides.get(idx);
        if (!g) {
            g = { times: [], places: [], pit };
            glides.set(idx, g);
        }
        if (g.times.length > 0 && now <= g.times[g.times.length - 1]) {
            return;
        }
        const known = g.places.length > 0 && g.places[0] >= 0 && g.places[g.places.length - 1] > -0.5;
        const last = known ? g.places[g.places.length - 1] : 0;
        const jump = !L || !L.smooth || pct < 0 || !known || pit !== g.pit || Math.abs(signed(pct, wrap(last))) > MAX_GLIDE;
        if (jump) {
            g.times.length = 0;
            g.places.length = 0;
            g.places.push(pct);
        } else {
            g.places.push(last + signed(pct, wrap(last)));
        }
        g.times.push(now);
        g.pit = pit;
        if (g.times.length > KEPT) {
            g.times.shift();
            g.places.shift();
        }
    }

    function shown(car, now) {
        const g = glides.get(car.i);
        if (!g || g.places.length === 0 || g.places[0] < 0) {
            return car.at;
        }
        const places = g.places;
        if (!L.smooth) {
            return wrap(places[places.length - 1]);
        }
        const times = g.times;
        const t = now - delay;
        if (t <= times[0]) {
            return wrap(places[0]);
        }
        for (let k = 1; k < times.length; k++) {
            if (t < times[k]) {
                const span = times[k] - times[k - 1];
                const f = span > 0 ? (t - times[k - 1]) / span : 1;
                return wrap(places[k - 1] + (places[k] - places[k - 1]) * f);
            }
        }
        return wrap(places[places.length - 1]);
    }

    // ------------------------------------------------------------------ geometry (as TrackShape and TrackMapOverlay)

    function shapeAt(pct) {
        const p = L.points;
        const n = p.length / 2;
        const f = L.closed ? wrap(pct) * n : Math.max(0, Math.min(1, pct)) * (n - 1);
        const i = Math.floor(f);
        const t = f - i;
        const a = L.closed ? i % n : Math.min(i, n - 1);
        const b = L.closed ? (i + 1) % n : Math.min(i + 1, n - 1);
        return [p[2 * a] + (p[2 * b] - p[2 * a]) * t, p[2 * a + 1] + (p[2 * b + 1] - p[2 * a + 1]) * t];
    }

    function shapeAcross(pct) {
        const a = shapeAt(pct + 0.002);
        const b = shapeAt(pct - 0.002);
        let x = a[0] - b[0];
        let y = a[1] - b[1];
        const len = Math.hypot(x, y);
        if (len < 1e-6) {
            return [0, 1];
        }
        x /= len;
        y /= len;
        return [-y, x];
    }

    function pitAt(pct) {
        if (!L.pit || !pitLen) {
            return null;
        }
        const span = wrap(L.pitExit - L.pitEntry);
        const into = wrap(pct - L.pitEntry);
        if (span <= 0 || into > span) {
            return null;
        }
        const total = pitLen[pitLen.length - 1];
        const target = into / span * total;
        const p = L.pit;
        for (let i = 1; i < pitLen.length; i++) {
            if (pitLen[i] >= target) {
                const piece = pitLen[i] - pitLen[i - 1];
                const t = piece > 0 ? (target - pitLen[i - 1]) / piece : 0;
                return [p[2 * i - 2] + (p[2 * i] - p[2 * i - 2]) * t, p[2 * i - 1] + (p[2 * i + 1] - p[2 * i - 1]) * t];
            }
        }
        return [p[p.length - 2], p[p.length - 1]];
    }

    function linePoint(fraction, cross) {
        const along = L.alongPad + fraction * L.length;
        return L.vertical ? [cross, L.height - along] : [along, cross];
    }

    function lineFraction(pct, playerPct, centred) {
        if (!centred || L.range <= 0) {
            return Math.max(0, Math.min(1, pct));
        }
        const d = signed(pct, playerPct);
        return Math.abs(d) <= L.range + 1e-9 ? 0.5 + d / (2 * L.range) : null;
    }

    const ringAngle = (pct, playerPct, centred) => centred ? L.turn * signed(pct, playerPct) * 360 : L.startAngle + L.turn * pct * 360;

    function ringPoint(angle, radius) {
        const rad = angle * Math.PI / 180;
        return [L.ringX + radius * Math.sin(rad), L.ringY - radius * Math.cos(rad)];
    }

    // ------------------------------------------------------------------ the track (once per layout)

    function shapePath(c, flat) {
        const p = flat;
        c.beginPath();
        c.moveTo(p[0], p[1]);
        for (let i = 2; i < p.length; i += 2) {
            c.lineTo(p[i], p[i + 1]);
        }
    }

    function trackPath(c) {
        if (L.mode === 'shape') {
            shapePath(c, L.points);
            if (L.closed) {
                c.closePath();
            }
        } else if (L.mode === 'ring') {
            c.beginPath();
            c.arc(L.ringX, L.ringY, L.ringR, 0, 2 * Math.PI);
        } else {
            const a = linePoint(0, L.cross);
            const b = linePoint(1, L.cross);
            c.beginPath();
            c.moveTo(a[0], a[1]);
            c.lineTo(b[0], b[1]);
        }
    }

    function drawTrack() {
        const layer = document.createElement('canvas');
        layer.width = canvas.width;
        layer.height = canvas.height;
        const c = layer.getContext('2d');
        c.scale(dpr, dpr);
        c.fillStyle = L.plate;
        c.beginPath();
        c.roundRect(0, 0, L.width, L.height, L.round);
        c.fill();
        c.lineJoin = 'round';
        if (L.mode === 'shape' && L.pit) {
            c.save();
            c.setLineDash([3, 3]);              // WPF dashes count in pen widths: 2 x 1.5 px
            c.lineWidth = 1.5;
            c.strokeStyle = L.pitLane;
            shapePath(c, L.pit);
            c.stroke();
            c.restore();
        }
        if (L.style === 'Strip') {
            if (L.mode === 'line') {
                const h = L.stripW;
                c.fillStyle = L.strip;
                c.beginPath();
                if (L.vertical) {
                    c.roundRect(L.cross - h / 2, L.alongPad - h / 2, h, L.length + h, Math.min(L.round, h / 2));
                } else {
                    c.roundRect(L.alongPad - h / 2, L.cross - h / 2, L.length + h, h, Math.min(L.round, h / 2));
                }
                c.fill();
            } else {
                c.lineWidth = L.stripW;
                c.strokeStyle = L.strip;
                trackPath(c);
                c.stroke();
            }
        } else if (L.style === 'Road') {
            // two thin lines along the track: a wide band with its middle cut out (on a layer of its own)
            const road = document.createElement('canvas');
            road.width = layer.width;
            road.height = layer.height;
            const r = road.getContext('2d');
            r.scale(dpr, dpr);
            r.lineJoin = 'round';
            r.lineCap = 'butt';
            if (L.roadFill) {
                // the fill between the two lines, in the line colour
                c.save();
                c.lineCap = 'butt';
                c.lineWidth = L.roadGap;
                c.strokeStyle = L.roadFill;
                trackPath(c);
                c.stroke();
                c.restore();
            }
            r.strokeStyle = L.road;
            trackPath(r);
            r.lineWidth = L.roadGap + 1.5;
            r.stroke();
            r.globalCompositeOperation = 'destination-out';
            r.strokeStyle = '#000';                 // opaque: the cut must remove the band completely
            r.lineWidth = L.roadGap - 1.5;
            r.stroke();
            c.drawImage(road, 0, 0, L.width, L.height);
        } else {
            c.lineWidth = L.thickness;
            c.lineCap = 'round';
            c.strokeStyle = L.line;
            trackPath(c);
            c.stroke();
        }
        return layer;
    }

    // ------------------------------------------------------------------ every frame

    SL.draw((now) => {
        if (!L || !track) {
            return;
        }
        if (!sample) {
            frames++;
        }
        if (now - statsSent >= 1) {
            // once a second: how often the map drew live cars and how far in the past it shows them (data check)
            if (frames > 0) {
                SL.send({ type: 'track-stats', frames, delay: L.smooth ? delay : 0 });
            }
            frames = 0;
            statsSent = now;
        }
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.drawImage(track, 0, 0);
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        const me = cars.find((c) => c.i === player);
        const hasPlayer = !!me;
        const centred = L.centred && hasPlayer && L.mode !== 'shape';
        const playerPct = hasPlayer ? shown(me, now) : 0;

        if (L.startLine !== 'Off') {
            if (L.mode === 'shape') {
                startLine(wrap(offset), now);
            } else if (L.mode === 'ring') {
                startLine(ringAngle(0, playerPct, centred), now);
            } else if (centred) {
                const f = lineFraction(0, playerPct, true);
                if (f !== null) {
                    startLine(f, now);
                }
            } else {
                startLine(0, now);
                startLine(1, now);
            }
        }

        const drawn = [];
        for (const car of cars) {
            const pct = shown(car, now);
            let at;
            if (L.mode === 'shape') {
                const onShape = wrap(pct + offset);
                at = (car.inPit && pitAt(onShape)) || shapeAt(onShape);
            } else if (L.mode === 'ring') {
                at = ringPoint(ringAngle(pct, playerPct, centred), L.ringR);
            } else {
                const f = lineFraction(pct, playerPct, centred);
                if (f === null) {
                    continue;
                }
                at = linePoint(f, L.cross);
            }
            const rank = car.p ? 3 : car.pit ? 0 : car.o ? 1 : 2;
            drawn.push({ car, at, rank, distance: hasPlayer ? Math.abs(signed(car.at, playerPct)) : 0 });
        }

        // on top where circles overlap: the player, the own class, other classes, cars in the pit. Right around the player the
        // closest lies on top; in a pack further away the better position, so the front of the pack stays readable.
        for (const d of drawn) {
            d.near = hasPlayer && d.distance <= NEAR_PLAYER;
        }
        drawn.sort((a, b) => a.rank - b.rank || (a.near - b.near) || (a.near ? b.distance - a.distance : 0) || b.car.pos - a.car.pos);
        if (L.radio && talking >= 0) {
            for (const d of drawn) {
                if (d.car.i === talking) {
                    waves(d, now);
                }
            }
        }
        for (const d of drawn) {
            drawCar(d.car, d.at);
        }
    });

    /** The start/finish line across the track: plain, or the Shiftline colours running along it. */
    function startLine(at, now) {
        const half = L.reach + 2;
        let centre;
        let across;
        if (L.mode === 'shape') {
            centre = shapeAt(at);
            across = shapeAcross(at);
        } else if (L.mode === 'ring') {
            centre = ringPoint(at, L.ringR);
            const out = ringPoint(at, L.ringR + 1);
            across = [out[0] - centre[0], out[1] - centre[1]];
        } else {
            centre = linePoint(at, L.cross);
            across = L.vertical ? [1, 0] : [0, 1];
        }
        const x0 = centre[0] - across[0] * half;
        const y0 = centre[1] - across[1] * half;
        const x1 = centre[0] + across[0] * half;
        const y1 = centre[1] + across[1] * half;
        let style = START[L.startLine];
        if (!style) {
            // one run of the four colours over the length of the line, moving along it once in FLOW_SECONDS
            const phase = (now % FLOW_SECONDS) / FLOW_SECONDS;
            const len = 2 * half;
            const sx = x0 - across[0] * len * (1 - phase);
            const sy = y0 - across[1] * len * (1 - phase);
            const g = ctx.createLinearGradient(sx, sy, sx + across[0] * 2 * len, sy + across[1] * 2 * len);
            for (let i = 0; i <= 8; i++) {
                g.addColorStop(i / 8, SHIFTLINE[i % 4]);
            }
            style = g;
        }
        ctx.lineWidth = 2;
        ctx.lineCap = 'butt';
        ctx.strokeStyle = style;
        ctx.beginPath();
        ctx.moveTo(x0, y0);
        ctx.lineTo(x1, y1);
        ctx.stroke();
    }

    /** Two thin rings running one after the other from the circle outwards and fading: on the radio. */
    function waves(d, now) {
        const r = d.car.p ? L.playerR : L.dot / 2;
        ctx.lineWidth = 1.5;
        ctx.strokeStyle = L.wave || d.car.c;
        for (let k = 0; k < 2; k++) {
            const x = wrap((now - k * WAVE_SECONDS / 2) / WAVE_SECONDS);
            const grow = 1 - (1 - x) * (1 - x);
            ctx.globalAlpha = 0.85 * (1 - x);
            ctx.beginPath();
            ctx.arc(d.at[0], d.at[1], r * (1 + WAVE_REACH * grow), 0, 2 * Math.PI);
            ctx.stroke();
        }
        ctx.globalAlpha = 1;
    }

    function circle(x, y, r) {
        ctx.beginPath();
        ctx.arc(x, y, Math.max(0, r), 0, 2 * Math.PI);
    }

    /** The glass edge: the class colour a little darker (75 %) and see-through (40 %). */
    function glass(colour) {
        const m = /rgba?\(([\d.]+),([\d.]+),([\d.]+)/.exec(colour || '');
        return m ? 'rgba(' + Math.floor(m[1] * 0.75) + ',' + Math.floor(m[2] * 0.75) + ',' + Math.floor(m[3] * 0.75) + ',0.4)' : 'rgba(47,126,183,0.4)';
    }

    function drawCar(car, at) {
        const [x, y] = at;
        const r = car.p ? L.playerR - RING_OUTSET : L.dot / 2;
        // one ring at most: the player's own (violet with the fastest lap), else a lap ring, else the fastest lap
        const ring = car.p ? (car.f ? FASTEST : PLAYER_RING) : car.l > 0 ? LAP_AHEAD : car.l < 0 ? LAP_BEHIND : car.f ? FASTEST : null;
        // the number gets the same room in every circle; a ring lies over the glass edge and a little outside (RING_OUTSET)
        const disc = r;
        const inner = r - 1.5;
        if (car.pit) {
            ctx.globalAlpha = 0.55;
        } else {
            ctx.fillStyle = car.c;
            circle(x, y, disc);
            ctx.fill();
        }
        if (ring) {
            ctx.lineWidth = 2.5;
            ctx.strokeStyle = ring;
            circle(x, y, r - 1.5 + 1.25);
            ctx.stroke();
            ctx.lineWidth = 1;
            ctx.strokeStyle = EDGE;
            circle(x, y, r + RING_OUTSET - 0.5);
            ctx.stroke();
        } else {
            ctx.lineWidth = 1.5;
            ctx.strokeStyle = glass(car.c);
            circle(x, y, r - 0.75);
            ctx.stroke();
        }
        if (car.pit) {
            ctx.lineWidth = 2;
            ctx.strokeStyle = car.c;
            circle(x, y, inner - 1);
            ctx.stroke();
        }
        if (car.t) {
            // the figures centred by their ink, not by the font's line height (as the WPF map draws their outline)
            const f = figure(car.t, 2 * inner);
            ctx.font = f.font;
            ctx.fillStyle = car.k;
            ctx.textAlign = 'left';
            ctx.textBaseline = 'alphabetic';
            ctx.fillText(car.t, x - f.cx, y - f.cy);
        }
        ctx.globalAlpha = 1;
    }

    /** Font and ink middle of a number for a disc: 55 % of the diameter high, smaller when wider than 82 % of it. */
    function figure(text, diameter) {
        const key = text + '|' + Math.round(diameter * 10);
        let f = figures.get(key);
        if (f) {
            return f;
        }
        const family = '"' + (L.font || 'Segoe UI') + '", "Segoe UI", sans-serif';
        let size = diameter * 0.55;
        ctx.font = '700 ' + size + 'px ' + family;
        let m = ctx.measureText(text);
        const room = diameter * 0.82;
        if (m.width > room) {
            size = size * room / m.width;
            ctx.font = '700 ' + size + 'px ' + family;
            m = ctx.measureText(text);
        }
        f = {
            font: ctx.font,
            cx: (m.actualBoundingBoxRight - m.actualBoundingBoxLeft) / 2,
            cy: (m.actualBoundingBoxDescent - m.actualBoundingBoxAscent) / 2,
        };
        figures.set(key, f);
        return f;
    }
})();
