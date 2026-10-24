(() => {
    const form = document.querySelector('#teacher-course-filters');
    document.querySelectorAll('[data-course-filter]').forEach(select => {
        select.addEventListener('change', () => form?.requestSubmit());
    });
    function openStatistics() {
        if (location.hash !== '#statistics') return;
        const statistics = document.querySelector('#statistics');
        if (statistics) {
            statistics.open = true;
            statistics.scrollIntoView({ block: 'start' });
        }
    }
    window.addEventListener('hashchange', openStatistics);
    openStatistics();
})();
