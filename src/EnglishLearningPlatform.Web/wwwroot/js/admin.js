(() => {
    document.querySelectorAll('[data-open-admin-dialog]').forEach(button => {
        button.addEventListener('click', () => document.getElementById(button.dataset.openAdminDialog)?.showModal());
    });
    document.querySelectorAll('[data-close-admin-dialog]').forEach(button => {
        button.addEventListener('click', () => button.closest('dialog').close());
    });
    document.querySelectorAll('.admin-dialog').forEach(dialog => {
        dialog.addEventListener('close', () => dialog.querySelector('form')?.reset());
    });
})();
