document.querySelectorAll('.profile-avatar-form').forEach(form => {
    const input = form.querySelector('input[type="file"]');
    const submit = form.querySelector('.profile-avatar-submit');
    const error = document.querySelector('#profile-avatar-error');
    if (!input || !submit) return;
    submit.hidden = true;
    input.addEventListener('change', () => {
        if (input.files[0]?.size > 2 * 1024 * 1024) {
            if (error) error.textContent = 'Ảnh đại diện không được vượt quá 2 MB.';
            input.value = '';
            return;
        }
        if (error) error.textContent = '';
        if (input.files.length) form.requestSubmit();
    });
});

document.querySelectorAll('[data-profile-tabs]').forEach(container => {
    const tabs = [...container.querySelectorAll('[role="tab"]')];
    const select = tab => {
        tabs.forEach(item => {
            const selected = item === tab;
            item.setAttribute('aria-selected', String(selected));
            item.tabIndex = selected ? 0 : -1;
            document.getElementById(item.getAttribute('aria-controls')).hidden = !selected;
        });
    };

    tabs.forEach((tab, index) => {
        tab.addEventListener('click', () => select(tab));
        tab.addEventListener('keydown', event => {
            let next;
            if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
            else if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
            else if (event.key === 'Home') next = 0;
            else if (event.key === 'End') next = tabs.length - 1;
            else return;
            event.preventDefault();
            select(tabs[next]);
            tabs[next].focus();
        });
    });

    select(tabs[0]);
    window.addEventListener('pageshow', () => select(tabs[0]));
});
