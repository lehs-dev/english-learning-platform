(() => {
    const form = document.querySelector('[data-course-editor]');
    if (form) {
        const title = form.querySelector('[data-course-title]');
        const description = form.querySelector('[data-course-description]');
        const level = form.querySelector('[data-course-level]');
        const price = form.querySelector('[data-price]');
        const priceTypes = form.querySelectorAll('[data-price-type]');
        const formatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 });

        title.addEventListener('input', () => title.setCustomValidity(''));
        title.addEventListener('invalid', () => {
            if (title.validity.valueMissing) title.setCustomValidity('Vui lòng nhập tên khóa học.');
        });
        price.addEventListener('input', () => price.setCustomValidity(''));
        price.addEventListener('invalid', () => {
            const message = price.validity.rangeOverflow ? 'Giá khóa học vượt quá giới hạn cho phép.'
                : price.validity.stepMismatch ? 'Giá khóa học tối đa 2 chữ số thập phân.'
                : 'Vui lòng nhập giá khóa học lớn hơn 0 đồng.';
            price.setCustomValidity(message);
        });

        function updatePreview() {
            form.querySelector('[data-course-title-count]').textContent = `${title.value.length}/200`;
            form.querySelector('[data-course-preview-title]').textContent = title.value.trim() || 'Tên khóa học của bạn';
            form.querySelector('[data-course-preview-description]').textContent = description.value.trim()
                || 'Mô tả ngắn giúp học viên hiểu nội dung và lựa chọn khóa học phù hợp.';
            form.querySelector('[data-course-preview-level]').textContent = level.selectedOptions[0]?.textContent.trim() || 'Chưa chọn cấp độ';
            const skills = Array.from(form.querySelectorAll('[data-course-skill]:checked'), input => {
                const tag = document.createElement('span');
                tag.textContent = input.closest('label').querySelector('span').textContent.trim();
                return tag;
            });
            form.querySelector('[data-course-preview-skills]').replaceChildren(...skills);
            const paid = form.querySelector('[data-price-type]:checked')?.value === 'true';
            const amount = price.valueAsNumber;
            form.querySelector('[data-course-preview-price]').textContent = !paid ? 'Miễn phí'
                : Number.isFinite(amount) && amount > 0 ? `${formatter.format(amount)} đồng` : 'Chưa nhập học phí';
            form.querySelector('[data-course-preview-pricing]').classList.toggle('is-paid', paid);
        }

        function updatePrice() {
            const paid = form.querySelector('[data-price-type]:checked')?.value === 'true';
            price.setCustomValidity('');
            price.readOnly = !paid;
            price.required = paid;
            price.min = paid ? '0.01' : '0';
            if (!paid) price.value = '0';
            form.querySelector('[data-course-price-help]').textContent = paid
                ? 'Nhập giá lớn hơn 0 đồng, tối đa 2 chữ số thập phân.' : 'Khóa học miễn phí có giá 0 đồng.';
            updatePreview();
        }
        priceTypes.forEach(input => input.addEventListener('change', updatePrice));
        form.addEventListener('input', updatePreview);
        form.addEventListener('change', updatePreview);

        // Keep the existing comma-separated field as the submitted value.
        const source = form.querySelector('[data-course-topics]');
        const editor = form.querySelector('[data-course-topics-editor]');
        const entry = document.createElement('input');
        entry.type = 'text';
        entry.id = 'course-topic-entry';
        entry.dataset.topicEntry = '';
        entry.placeholder = '+ Thêm chủ đề';
        entry.setAttribute('aria-describedby', 'course-topics-help course-topics-error');
        const splitTopics = text => text.split(',').map(topic => topic.trim()).filter(Boolean);
        let topics = splitTopics(source.value);
        function renderTopics() {
            editor.querySelectorAll('.course-topic-chip').forEach(chip => chip.remove());
            for (const [index, topic] of topics.entries()) {
                const chip = document.createElement('span');
                chip.className = 'course-topic-chip';
                const text = document.createElement('span');
                text.textContent = topic;
                const remove = document.createElement('button');
                remove.type = 'button';
                remove.textContent = '×';
                remove.setAttribute('aria-label', `Xóa chủ đề ${topic}`);
                remove.addEventListener('click', () => {
                    topics.splice(index, 1);
                    source.value = topics.join(', ');
                    renderTopics();
                    entry.setCustomValidity('');
                    entry.focus();
                });
                chip.append(text, remove);
                editor.insertBefore(chip, entry);
            }
        }
        function commitTopics() {
            if (!entry.value.trim()) {
                entry.setCustomValidity('');
                return true;
            }
            const proposed = [...topics];
            for (const topic of splitTopics(entry.value)) {
                if (!proposed.some(existing => existing.toLocaleLowerCase('vi-VN') === topic.toLocaleLowerCase('vi-VN'))) proposed.push(topic);
            }
            const error = proposed.some(topic => topic.length > 120) ? 'Mỗi chủ đề tối đa 120 ký tự.'
                : proposed.join(', ').length > source.maxLength ? 'Danh sách chủ đề tối đa 4.000 ký tự.' : '';
            entry.setCustomValidity(error);
            if (error) return false;
            topics = proposed;
            source.value = topics.join(', ');
            entry.value = '';
            renderTopics();
            return true;
        }
        entry.addEventListener('keydown', event => {
            if (!event.isComposing && (event.key === 'Enter' || event.key === ',')) {
                event.preventDefault();
                if (!commitTopics()) entry.reportValidity();
            }
        });
        entry.addEventListener('input', event => {
            entry.setCustomValidity('');
            if (!event.isComposing && entry.value.includes(',')) commitTopics();
        });
        entry.addEventListener('blur', event => {
            // Let a chip's remove button handle its click before changing the chip DOM.
            if (!editor.contains(event.relatedTarget)) commitTopics();
        });
        form.addEventListener('submit', event => {
            if (!commitTopics()) {
                event.preventDefault();
                entry.reportValidity();
            }
        });
        source.hidden = true;
        editor.classList.add('is-enhanced');
        editor.append(entry);
        form.querySelector('[data-course-topics-label]').htmlFor = entry.id;
        renderTopics();
        updatePrice();
        // Refresh restored values when returning through browser history.
        window.addEventListener('pageshow', updatePrice);
    }

    // The lesson resource form also loads this script.
    const resourceType = document.querySelector('[data-resource-type]');
    function updateResource() {
        if (!resourceType) return;
        const text = resourceType.value === '1';
        document.querySelector('[data-text-fields]').hidden = !text;
        document.querySelector('[data-url-fields]').hidden = text;
    }
    resourceType?.addEventListener('change', updateResource);
    updateResource();
})();
