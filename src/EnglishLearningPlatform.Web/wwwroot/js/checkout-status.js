(() => {
    const root = document.querySelector('[data-checkout-status-url][data-checkout-pending="true"]');
    if (!root) return;
    const message = root.querySelector('[data-checkout-message]');
    const payLink = root.querySelector('[data-vnpay-pay-link="true"]');
    const attemptKey = root.dataset.vnpayOrderId ? 'vnpay-started:' + root.dataset.vnpayOrderId : null;
    let previouslyStarted = false;
    try { previouslyStarted = attemptKey && sessionStorage.getItem(attemptKey) === 'true'; } catch { }
    if (payLink && previouslyStarted) { payLink.hidden = true; payLink.style.display = 'none'; }
    payLink?.addEventListener('click', () => { try { if (attemptKey) sessionStorage.setItem(attemptKey, 'true'); } catch { } });
    let stopped = false, polling = false, timer, attempts = 0, loginExpired = false;
    const notify = (text) => { if (message) message.textContent = text; };
    async function poll() {
        if (loginExpired || polling || stopped || document.hidden) return;
        polling = true;
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 10000);
        try {
            const response = await fetch(root.dataset.checkoutStatusUrl, {
                credentials: 'same-origin', cache: 'no-store', redirect: 'manual', signal: controller.signal
            });
            if (response.status === 401 || response.type === 'opaqueredirect' || response.status === 302) {
                stopped = true; loginExpired = true;
                notify('Phiên đăng nhập đã hết hạn. Đăng nhập lại để xem kết quả; không cần thanh toán lại.');
                return;
            }
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) {
                if (response.status === 403 || response.status === 404) stopped = true;
                notify('Chưa kiểm tra được đơn hàng. Vui lòng thử lại hoặc mở Lịch sử giao dịch.');
                return;
            }
            const state = await response.json();
            if (state.status || state.canLearn || (state.expired && root.dataset.checkoutExpired !== 'true')) {
                const destination = new URL(state.resultUrl, location.href);
                if (destination.origin !== location.origin) { notify('Không thể mở trang kết quả. Vui lòng mở Lịch sử giao dịch.'); return; }
                stopped = true;
                notify('Đã cập nhật trạng thái. Đang mở kết quả…');
                location.replace(destination.href);
                return;
            }
            const checkedAt = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date());
            notify(state.expired
                ? `Đơn đã hết hạn, chưa ghi nhận thanh toán. Nếu VNPay đã báo thành công, không thanh toán lại. Kiểm tra lúc ${checkedAt}.`
                : `Chưa nhận được xác nhận thanh toán. Nếu đang thanh toán trên VNPay, hãy hoàn tất và quay về Estudy. Nếu VNPay đã báo thành công, không thanh toán lại. Kiểm tra lúc ${checkedAt}.`);
        } catch {
            notify('Mất kết nối với hệ thống. Trang sẽ tự thử lại. Bạn có thể xem trong Lịch sử giao dịch; không thanh toán lại nếu VNPay đã báo thành công.');
        } finally {
            clearTimeout(timeout);
            polling = false;
        }
    }
    function schedule() {
        clearTimeout(timer);
        if (stopped) return;
        if (attempts >= 60) {
            notify('Chưa nhận được xác nhận thanh toán. Vui lòng hoàn tất trên VNPay rồi quay về Estudy, hoặc xem Lịch sử giao dịch. Không thanh toán lại nếu VNPay đã báo thành công.');
            return;
        }
        timer = setTimeout(async () => {
            if (!document.hidden) { attempts++; await poll(); }
            schedule();
        }, attempts < 10 ? 2000 : 5000);
    }
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { poll(); schedule(); } });
    window.addEventListener('pagehide', () => { stopped = true; clearTimeout(timer); }, { once: true });
    window.addEventListener('pageshow', (event) => { if (event.persisted) location.reload(); });
    poll(); schedule();
})();
