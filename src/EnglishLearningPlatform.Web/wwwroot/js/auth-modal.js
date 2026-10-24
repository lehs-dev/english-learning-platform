(() => {
    const dialog = document.querySelector("[data-auth-dialog]");
    if (!dialog || typeof dialog.showModal !== "function") return;

    const content = dialog.querySelector("[data-auth-content]");
    let opener;
    let controller;
    let requestId = 0;
    let submitting = false;
    const directPanel = document.querySelector("main [data-auth-panel]");
    const isAuthUrl = (url) => url.origin === location.origin &&
        /\/account\/(login|register)\/?$/i.test(url.pathname);

    function show(trigger) {
        if (dialog.open) return;
        opener = trigger || document.activeElement;
        document.querySelectorAll('[data-nav-toggle][aria-expanded="true"]').forEach((toggle) => toggle.click());
        dialog.showModal();
        document.body.classList.add("auth-modal-open");
    }

    function render(panel) {
        content.replaceChildren(panel);
        dialog.scrollTop = 0;
        const error = panel.querySelector(".validation-summary-errors, .input-validation-error, .notice-success");
        const focusTarget = error || panel.querySelector("input:not([type='hidden'])") || panel.querySelector("h1");
        if (focusTarget) {
            if (!focusTarget.matches("input")) focusTarget.tabIndex = -1;
            focusTarget.focus();
        }
    }

    function showError(url, message) {
        content.querySelector("[data-auth-error]")?.remove();
        content.querySelector("[data-auth-loading]")?.remove();
        const notice = document.createElement("div");
        notice.className = "notice notice-danger auth-modal-error";
        notice.dataset.authError = "";
        notice.setAttribute("role", "alert");
        notice.tabIndex = -1;
        const link = document.createElement("a");
        link.href = url;
        link.dataset.authFallback = "";
        link.textContent = "Mở lại biểu mẫu";
        notice.append(document.createTextNode(`${message} `), link);
        (content.querySelector(".auth-form-panel") || content).prepend(notice);
        notice.focus();
    }

    async function load(url, form) {
        controller?.abort();
        controller = new AbortController();
        const currentRequest = ++requestId;
        submitting = Boolean(form);
        const submitButton = form?.querySelector("button[type='submit']");
        if (submitButton) submitButton.disabled = true;
        content.setAttribute("aria-busy", "true");
        try {
            const response = await fetch(url, {
                method: form ? "POST" : "GET",
                body: form ? new FormData(form) : undefined,
                credentials: "same-origin",
                headers: { "X-Auth-Modal": "true" },
                signal: controller.signal
            });
            if (currentRequest !== requestId || !dialog.open) return;
            if (!response.ok) throw new Error(response.status === 400 ? "expired-form" : "request-failed");

            if (response.headers.get("content-type")?.includes("application/json")) {
                const result = await response.json();
                if (currentRequest !== requestId || !dialog.open) return;
                if (typeof result.redirectUrl !== "string") throw new Error("missing-redirect");
                const destination = new URL(result.redirectUrl, location.href);
                if (destination.origin !== location.origin) throw new Error("invalid-redirect");
                location.assign(destination.href);
                return;
            }

            const html = await response.text();
            if (currentRequest !== requestId || !dialog.open) return;
            const page = new DOMParser().parseFromString(html, "text/html");
            const panel = page.querySelector("[data-auth-panel]");
            if (!panel) throw new Error("missing-form");
            render(document.importNode(panel, true));
        } catch (error) {
            if (error.name !== "AbortError" && currentRequest === requestId && dialog.open) {
                const retryUrl = new URL(url, location.href);
                const returnUrl = form?.elements.namedItem("ReturnUrl")?.value;
                if (returnUrl) retryUrl.searchParams.set("returnUrl", returnUrl);
                showError(retryUrl.href, error.message === "expired-form"
                    ? "Phiên biểu mẫu đã hết hạn. Vui lòng mở lại biểu mẫu để tiếp tục."
                    : "Không thể tải biểu mẫu. Vui lòng kiểm tra kết nối và thử lại.");
            }
        } finally {
            if (currentRequest === requestId) {
                submitting = false;
                content.removeAttribute("aria-busy");
                if (submitButton) submitButton.disabled = false;
            }
        }
    }

    document.addEventListener("click", (event) => {
        const link = event.target.closest("a[href]");
        if (!link || link.hasAttribute("data-auth-fallback") || link.hasAttribute("download") ||
            (link.target && link.target !== "_self") || event.button !== 0 ||
            event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        const url = new URL(link.href);
        if (!isAuthUrl(url)) return;
        event.preventDefault();
        if (submitting) return;
        show(link);
        if (!content.querySelector("[data-auth-panel]")) {
            const heading = document.createElement("h1");
            heading.id = "auth-title";
            heading.textContent = /\/register\/?$/i.test(url.pathname) ? "Tạo tài khoản" : "Đăng nhập";
            const loading = document.createElement("p");
            loading.dataset.authLoading = "";
            loading.setAttribute("role", "status");
            loading.textContent = "Đang tải biểu mẫu…";
            content.replaceChildren(heading, loading);
        }
        void load(url.href);
    });

    dialog.addEventListener("submit", (event) => {
        const form = event.target;
        if (!form.matches(".account-form") || !isAuthUrl(new URL(form.action))) return;
        event.preventDefault();
        if (!submitting) void load(form.action, form);
    });

    dialog.querySelector("[data-auth-close]").addEventListener("click", () => dialog.close());
    let backdropPressed = false;
    const outside = (event) => {
        const bounds = dialog.getBoundingClientRect();
        return event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom;
    };
    dialog.addEventListener("pointerdown", (event) => { backdropPressed = event.target === dialog && outside(event); });
    dialog.addEventListener("click", (event) => {
        if (backdropPressed && event.target === dialog && outside(event)) dialog.close();
        backdropPressed = false;
    });
    dialog.addEventListener("close", () => {
        controller?.abort();
        requestId++;
        submitting = false;
        content.removeAttribute("aria-busy");
        document.body.classList.remove("auth-modal-open");
        if (directPanel) {
            location.assign(dialog.dataset.homeUrl || "/");
        } else {
            content.replaceChildren();
            if (opener?.isConnected) opener.focus();
        }
    });

    document.addEventListener("click", (event) => {
        const button = event.target.closest("[data-demo-email]");
        if (!button) return;
        const panel = button.closest("[data-auth-panel]");
        const email = panel.querySelector("input[name='Email']");
        const passwordInput = panel.querySelector("input[name='Password']");
        email.value = button.dataset.demoEmail;
        passwordInput.value = button.closest("[data-demo-password]").dataset.demoPassword || "";
        [email, passwordInput].forEach((input) => {
            input.dispatchEvent(new Event("input", { bubbles: true }));
            input.dispatchEvent(new Event("change", { bubbles: true }));
        });
        panel.querySelectorAll("[data-demo-email]").forEach((item) => item.setAttribute("aria-pressed", String(item === button)));
        (passwordInput.value ? panel.querySelector("button[type='submit']") : passwordInput).focus();
    });

    if (directPanel) {
        content.append(directPanel);
        document.querySelector(".auth-copyright")?.setAttribute("hidden", "");
        show();
        render(directPanel);
    }
})();
