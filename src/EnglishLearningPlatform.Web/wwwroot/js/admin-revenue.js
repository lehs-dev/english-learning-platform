(() => {
    'use strict';
    const select = document.querySelector('#period');
    const from = document.querySelector('#fromDate');
    const to = document.querySelector('#toDate');
    if (select && from && to) {
        const date = new Date(`${select.dataset.vietnamToday}T00:00:00Z`);
        const iso = value => value.toISOString().slice(0, 10);
        select.addEventListener('change', () => {
            const year = date.getUTCFullYear(), month = date.getUTCMonth();
            let start, end;
            if (select.value === 'week') {
                start = new Date(date);
                start.setUTCDate(start.getUTCDate() - (start.getUTCDay() + 6) % 7);
                end = new Date(start); end.setUTCDate(end.getUTCDate() + 6);
            } else if (select.value === 'month') {
                start = new Date(Date.UTC(year, month, 1)); end = new Date(Date.UTC(year, month + 1, 0));
            } else if (select.value === 'year') {
                start = new Date(Date.UTC(year, 0, 1)); end = new Date(Date.UTC(year, 11, 31));
            } else if (select.value === 'all') { from.value = ''; to.value = ''; return; }
            if (start) { from.value = iso(start); to.value = iso(end); }
        });
        for (const input of [from, to]) input.addEventListener('input', () => { select.value = 'custom'; });
    }
    for (const figure of document.querySelectorAll('[data-revenue-line-chart]')) {
        const points = JSON.parse(figure.querySelector('.admin-chart-data').textContent);
        if (!points.length) continue;
        const ns = 'http://www.w3.org/2000/svg';
        const node = (name, attributes, text) => {
            const element = document.createElementNS(ns, name);
            for (const [key, value] of Object.entries(attributes || {})) element.setAttribute(key, String(value));
            if (text !== undefined) element.textContent = text;
            return element;
        };
        const svg = node('svg', { viewBox: '0 0 1000 310', role: 'img', 'aria-label': 'Biểu đồ đường doanh thu VND; số liệu chi tiết trong bảng bên dưới.' });
        const maximum = Math.max(1, ...points.map(p => p.amount));
        const x = index => points.length === 1 ? 540 : 100 + index * 860 / (points.length - 1);
        const y = amount => 250 - amount / maximum * 215;
        const money = value => new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value);
        for (let tick = 0; tick <= 4; tick++) {
            const amount = maximum * tick / 4, height = y(amount);
            svg.append(node('line', { x1: 100, x2: 960, y1: height, y2: height, class: 'admin-chart-grid' }));
            svg.append(node('text', { x: 88, y: height + 4, 'text-anchor': 'end' }, money(amount)));
        }
        svg.append(node('polyline', { points: points.map((p, i) => `${x(i)},${y(p.amount)}`).join(' '), class: 'admin-chart-line' }));
        const ticks = [...new Set(Array.from({ length: Math.min(6, points.length) }, (_, i) => Math.round(i * (points.length - 1) / Math.max(1, Math.min(6, points.length) - 1))))];
        for (const index of ticks) svg.append(node('text', { x: x(index), y: 282, 'text-anchor': 'middle' }, points[index].label));
        const output = figure.querySelector('output');
        points.forEach((point, index) => {
            const text = `${point.label}: ${money(point.amount)} ₫ · ${point.orders} đơn thanh toán`;
            const circle = node('circle', { cx: x(index), cy: y(point.amount), r: points.length > 90 ? 2.5 : 5, class: 'admin-chart-point' });
            circle.append(node('title', {}, text));
            circle.addEventListener('pointerenter', () => { output.textContent = text; });
            circle.addEventListener('click', () => { output.textContent = text; });
            svg.append(circle);
        });
        output.textContent = 'Chọn một điểm trên biểu đồ để xem doanh thu.';
        figure.querySelector('.admin-line-chart-canvas').replaceChildren(svg);
    }
})();
