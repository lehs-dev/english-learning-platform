(() => {
    // Assessment library filters
    document.querySelectorAll('[data-assessment-library]').forEach(root => {
        const searchInput = root.querySelector('[data-library-search]');
        const scopeButtons = [...root.querySelectorAll('[data-scope-filter]')];
        const skillButtons = [...root.querySelectorAll('[data-skill-filter]')];
        const cards = [...root.querySelectorAll('[data-library-card]')];
        const countEl = root.querySelector('[data-library-count]');
        const gridEl = root.querySelector('[data-library-grid]');
        const emptyEl = root.querySelector('[data-library-empty]');
        const resetBtn = root.querySelector('[data-library-reset]');

        let scope = 'all';
        let skill = 'all';
        let query = '';

        const setPressed = (buttons, value, attr) => {
            buttons.forEach(btn => {
                const active = btn.getAttribute(attr) === value;
                btn.classList.toggle('is-active', active);
                btn.setAttribute('aria-pressed', active ? 'true' : 'false');
            });
        };

        const apply = () => {
            let visible = 0;
            const normalized = query.trim().toLowerCase();
            cards.forEach(card => {
                const title = card.dataset.title || '';
                const cardScope = card.dataset.scope || '';
                const isActive = card.dataset.active === 'true';
                const cardSkills = (card.dataset.skills || '').split(',').filter(Boolean);

                const matchesQuery = !normalized || title.includes(normalized);
                const matchesScope = scope === 'all'
                    || (scope === 'active' && isActive)
                    || cardScope === scope;
                const matchesSkill = skill === 'all' || cardSkills.includes(skill);

                const show = matchesQuery && matchesScope && matchesSkill;
                card.hidden = !show;
                if (show) visible++;
            });

            if (countEl) countEl.textContent = `${visible} / ${cards.length} bài`;
            if (gridEl) gridEl.hidden = visible === 0;
            if (emptyEl) emptyEl.hidden = visible > 0;
        };

        searchInput?.addEventListener('input', () => {
            query = searchInput.value;
            apply();
        });

        scopeButtons.forEach(btn => btn.addEventListener('click', () => {
            scope = btn.getAttribute('data-scope-filter') || 'all';
            setPressed(scopeButtons, scope, 'data-scope-filter');
            apply();
        }));

        skillButtons.forEach(btn => btn.addEventListener('click', () => {
            skill = btn.getAttribute('data-skill-filter') || 'all';
            setPressed(skillButtons, skill, 'data-skill-filter');
            apply();
        }));

        resetBtn?.addEventListener('click', () => {
            scope = 'all';
            skill = 'all';
            query = '';
            if (searchInput) searchInput.value = '';
            setPressed(scopeButtons, 'all', 'data-scope-filter');
            setPressed(skillButtons, 'all', 'data-skill-filter');
            apply();
        });
    });

    // Course outline filters & expand/collapse all
    document.querySelectorAll('[data-course-outline-tools]').forEach(toolbar => {
        const container = toolbar.closest('.course-room-section') || document;
        const filterButtons = [...toolbar.querySelectorAll('[data-outline-filter]')];
        const toggleAllBtn = toolbar.querySelector('[data-outline-toggle-all]');
        const modules = [...container.querySelectorAll('.outline-module')];
        const emptyNotice = container.querySelector('[data-outline-empty-filter]');
        let currentFilter = 'all';

        const applyFilter = () => {
            let visibleLessonsTotal = 0;
            modules.forEach(moduleEl => {
                const items = [...moduleEl.querySelectorAll('[data-lesson-completed]')];
                if (items.length === 0) {
                    moduleEl.hidden = currentFilter !== 'all';
                    return;
                }
                let visibleInModule = 0;
                items.forEach(item => {
                    const completed = item.getAttribute('data-lesson-completed') === 'true';
                    const show = currentFilter === 'all'
                        || (currentFilter === 'completed' && completed)
                        || (currentFilter === 'remaining' && !completed);
                    item.hidden = !show;
                    if (show) visibleInModule++;
                });
                moduleEl.hidden = visibleInModule === 0;
                if (currentFilter !== 'all' && visibleInModule > 0) {
                    moduleEl.open = true;
                }
                visibleLessonsTotal += visibleInModule;
            });
            if (emptyNotice) emptyNotice.hidden = visibleLessonsTotal > 0;
        };

        filterButtons.forEach(btn => btn.addEventListener('click', () => {
            currentFilter = btn.getAttribute('data-outline-filter') || 'all';
            filterButtons.forEach(other => {
                const active = other === btn;
                other.classList.toggle('is-active', active);
                other.setAttribute('aria-pressed', active ? 'true' : 'false');
            });
            applyFilter();
        }));

        toggleAllBtn?.addEventListener('click', () => {
            const visibleModules = modules.filter(m => !m.hidden);
            const allOpen = visibleModules.length > 0 && visibleModules.every(m => m.open);
            visibleModules.forEach(m => { m.open = !allOpen; });
            toggleAllBtn.textContent = allOpen ? 'Mở tất cả chương' : 'Thu gọn tất cả';
        });
    });
})();
