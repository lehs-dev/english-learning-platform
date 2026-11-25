(() => {
    const group = document.querySelector('[data-landing-filter]');
    if (!group) return;

    const buttons = [...group.querySelectorAll('[data-filter]')];
    const cards = [...document.querySelectorAll('.homepage-courses-grid [data-skill]')]
        .map(card => ({ element: card, skills: card.dataset.skill.split(/\s+/) }));
    const empty = document.querySelector('[data-landing-filter-empty]');
    const catalogLinks = [...document.querySelectorAll('[data-landing-catalog-link]')];
    const count = document.querySelector('[data-landing-filter-count]');

    group.addEventListener('click', event => {
        const selected = event.target.closest('[data-filter]');
        if (!selected) return;
        const skill = selected.dataset.filter;

        for (const button of buttons) {
            const active = button === selected;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', String(active));
        }
        let shown = 0;
        for (const { element, skills } of cards) {
            const visible = skill === 'all' || skills.includes(skill);
            element.toggleAttribute('data-filter-enter', visible && element.hidden);
            element.hidden = !visible;
            if (visible) shown++;
        }

        for (const link of catalogLinks) {
            const catalog = new URL(link.href);
            if (skill === 'all') catalog.searchParams.delete('Skill');
            else catalog.searchParams.set('Skill', skill);
            link.href = catalog.href;
        }
        empty.hidden = shown !== 0;
        count.textContent = shown > 0 ? `Hiển thị ${shown} khóa học.` : 'Không có khóa học nào trên trang khớp với bộ lọc.';
    });
    group.hidden = false;
})();

(() => {
    const page = document.querySelector('.landing-page');
    if (!page) return;

    const hero = page.querySelector('.homepage-hero-card');
    const header = document.querySelector('.site-header');
    const dock = page.querySelector('[data-landing-dock]');
    const navLinks = [...header.querySelectorAll('.main-nav a[href^="#"]')];
    const sections = new Map(navLinks.map(link => [link.hash, document.getElementById(link.hash.slice(1))]));
    const practice = sections.get('#practice');
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const mobile = window.matchMedia('(max-width: 760px)');

    let activeHash;
    function updateNavigation(hash) {
        if (hash === activeHash) return;
        activeHash = hash;
        for (const link of navLinks) {
            if (link.hash === hash) link.setAttribute('aria-current', 'location');
            else link.removeAttribute('aria-current');
        }
    }

    document.addEventListener('click', event => {
        if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest('a[href^="#"]');
        const section = link && sections.get(link.hash);
        if (!section) return;

        event.preventDefault();
        if (location.hash !== link.hash) history.pushState(null, '', link.hash);

        const headingId = section.getAttribute('aria-labelledby');
        const focusTarget = document.getElementById(headingId) ?? section;
        if (!focusTarget.hasAttribute('tabindex')) focusTarget.tabIndex = -1;
        focusTarget.focus({ preventScroll: true });
        section.scrollIntoView({ behavior: reducedMotion.matches ? 'instant' : 'smooth', block: 'start' });
        updateNavigation(link.hash);
    });

    if ('IntersectionObserver' in window) {
        let heroVisible = true;
        let practiceVisible = true;
        const updateDock = () => {
            dock.hidden = !mobile.matches || heroVisible || practiceVisible;
            header.classList.toggle('is-scrolled', !heroVisible);
        };
        const dockObserver = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (entry.target === hero) heroVisible = entry.isIntersecting;
                if (entry.target === practice) practiceVisible = entry.isIntersecting;
            }
            updateDock();
        }, { rootMargin: '-62px 0px -66px 0px' });
        dockObserver.observe(hero);
        dockObserver.observe(practice);
        mobile.addEventListener('change', updateDock);

        const entrance = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                if (!reducedMotion.matches) entry.target.classList.add('landing-in-view');
                entrance.unobserve(entry.target);
            }
        }, { threshold: .12 });
        for (const section of page.querySelectorAll(':scope > section:not(.homepage-hero-card)')) entrance.observe(section);
    }

    const orderedSections = [...page.querySelectorAll(':scope > section[id]')]
        .filter(section => sections.has(`#${section.id}`)).reverse();
    let navigationFrame = 0;

    function updateLocation() {
        const marker = Math.min(window.innerHeight * .32, 220);
        const selected = orderedSections.find(section => section.getBoundingClientRect().top <= marker);
        updateNavigation(selected ? `#${selected.id}` : '');
        navigationFrame = 0;
    }

    window.addEventListener('scroll', () => {
        if (!navigationFrame) navigationFrame = requestAnimationFrame(updateLocation);
    }, { passive: true });
    updateLocation();

    const art = page.querySelector('[data-landing-art]');
    const finePointer = window.matchMedia('(hover: hover) and (pointer: fine)');
    let bounds;
    let frame = 0;
    let cursorX;
    let cursorY;

    function resetTilt() {
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
        bounds = undefined;
        art.removeAttribute('data-art-active');
        art.style.removeProperty('--art-x');
        art.style.removeProperty('--art-y');
        art.style.removeProperty('--art-light-x');
        art.style.removeProperty('--art-light-y');
    }

    art.addEventListener('pointerenter', () => {
        if (!finePointer.matches || reducedMotion.matches) return;
        bounds = art.getBoundingClientRect();
        art.setAttribute('data-art-active', '');
    });
    art.addEventListener('pointermove', event => {
        if (!bounds) return;
        cursorX = event.clientX;
        cursorY = event.clientY;
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            const x = (cursorX - bounds.left) / bounds.width - .5;
            const y = (cursorY - bounds.top) / bounds.height - .5;
            art.style.setProperty('--art-x', `${-y * 9}deg`);
            art.style.setProperty('--art-y', `${x * 10}deg`);
            art.style.setProperty('--art-light-x', `${x * 65}%`);
            art.style.setProperty('--art-light-y', `${y * 65}%`);
        });
    });
    art.addEventListener('pointerleave', resetTilt);
    window.addEventListener('resize', resetTilt, { passive: true });
    reducedMotion.addEventListener('change', resetTilt);
    finePointer.addEventListener('change', resetTilt);
})();
