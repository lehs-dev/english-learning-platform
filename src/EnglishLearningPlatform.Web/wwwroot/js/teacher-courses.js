(() => {
    const form = document.querySelector('#teacher-course-filters');
    document.querySelectorAll('[data-course-filter]').forEach(select => {
        select.addEventListener('change', () => form?.requestSubmit());
    });
})();
