document.querySelectorAll('[data-catalog-filters]').forEach(filters => {
    const mobile = window.matchMedia('(max-width: 760px)');
    const sync = () => { filters.open = !mobile.matches; };
    mobile.addEventListener('change', sync);
    sync();
});
