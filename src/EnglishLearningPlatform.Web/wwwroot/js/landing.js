(() => {
    const group = document.querySelector('[data-landing-filter]');
    const empty = document.querySelector('[data-landing-filter-empty]');
    if (!group || !empty) return;

    const buttons = Array.from(group.querySelectorAll('[data-filter]'));
    const cards = Array.from(document.querySelectorAll('.homepage-courses-grid [data-skill]'));

    group.addEventListener('click', (event) => {
        const selected = event.target.closest('[data-filter]');
        if (!selected) return;

        for (const button of buttons) {
            const active = button === selected;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', String(active));
        }

        const skill = selected.dataset.filter;
        let shown = 0;
        for (const card of cards) {
            card.hidden = skill !== 'all' && !card.dataset.skill.split(' ').includes(skill);
            if (!card.hidden) shown++;
        }
        empty.hidden = shown !== 0;
    });
    group.hidden = false;
})();
