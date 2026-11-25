document.querySelectorAll("[data-dictionary-widget]").forEach(widget => {
    const lookupUrl = widget.dataset.lookupUrl;
    const form = widget.querySelector("[data-dict-form]");
    const input = widget.querySelector("[data-dict-input]");
    const submitBtn = widget.querySelector("[data-dict-submit]");
    const dirBtns = [...widget.querySelectorAll("[data-dict-dir]")];
    const sampleBtns = [...widget.querySelectorAll("[data-dict-sample]")];
    const statusEl = widget.querySelector("[data-dict-status]");
    const resultEl = widget.querySelector("[data-dict-result]");
    const wordEl = widget.querySelector("[data-dict-word]");
    const phoneticEl = widget.querySelector("[data-dict-phonetic]");
    const audioBtn = widget.querySelector("[data-dict-audio-btn]");
    const transEl = widget.querySelector("[data-dict-translation]");
    const altWrapEl = widget.querySelector("[data-dict-alt-wrap]");
    const altsEl = widget.querySelector("[data-dict-alts]");
    const groupsEl = widget.querySelector("[data-dict-groups]");
    const sourceEl = widget.querySelector("[data-dict-source]");

    const historyListEl = widget.querySelector("[data-dict-history-list]");
    const historyEmptyEl = widget.querySelector("[data-dict-history-empty]");
    const clearHistoryBtn = widget.querySelector("[data-dict-clear-history]");
    const historyKey = "estudy.dictionary.history.v1";

    let direction = "en-vi";
    let currentAudioUrl = "";

    const setDirection = nextDir => {
        direction = nextDir === "vi-en" ? "vi-en" : "en-vi";
        dirBtns.forEach(other => {
            const active = other.dataset.dictDir === direction;
            other.classList.toggle("is-active", active);
            other.setAttribute("aria-pressed", active ? "true" : "false");
        });
        if (input) {
            input.placeholder = direction === "en-vi"
                ? "Nhập từ hoặc cụm từ tiếng Anh (vd: victim, carrot, deadline)..."
                : "Nhập từ hoặc cụm từ tiếng Việt (vd: thành công, kinh nghiệm)...";
        }
    };

    const loadHistory = () => {
        try {
            const parsed = JSON.parse(localStorage.getItem(historyKey) || "[]");
            return Array.isArray(parsed) ? parsed : [];
        } catch {
            return [];
        }
    };

    const saveHistory = items => {
        try {
            localStorage.setItem(historyKey, JSON.stringify(items.slice(0, 10)));
        } catch {
            // Ignore storage errors in private mode
        }
    };

    const renderHistory = () => {
        if (!historyListEl || !historyEmptyEl || !clearHistoryBtn) return;
        const items = loadHistory();
        historyListEl.replaceChildren();
        items.forEach(item => {
            const li = document.createElement("li");
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "dictionary-history-item";
            const word = document.createElement("strong");
            word.textContent = item.query;
            const trans = document.createElement("span");
            trans.textContent = item.translation;
            btn.append(word, trans);
            btn.addEventListener("click", () => {
                setDirection(item.direction || "en-vi");
                if (input) input.value = item.query;
                performLookup(item.query);
            });
            li.appendChild(btn);
            historyListEl.appendChild(li);
        });
        historyEmptyEl.hidden = items.length > 0;
        historyListEl.hidden = items.length === 0;
        clearHistoryBtn.hidden = items.length === 0;
    };

    const addToHistory = (query, translation, dir) => {
        const items = loadHistory().filter(item =>
            !(item.query.toLowerCase() === query.toLowerCase() && item.direction === dir));
        items.unshift({ query, translation, direction: dir });
        saveHistory(items);
        renderHistory();
    };

    const makeClickableWordChip = (word, variantClass = "") => {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = `dict-word-chip ${variantClass}`.trim();
        btn.textContent = word;
        btn.title = `Tra từ "${word}"`;
        btn.addEventListener("click", () => {
            setDirection("en-vi");
            if (input) input.value = word;
            performLookup(word);
        });
        return btn;
    };

    const performLookup = async rawQuery => {
        const query = (rawQuery || "").trim();
        if (!query) {
            if (statusEl) {
                statusEl.textContent = "Hãy nhập từ hoặc cụm từ cần tra.";
                statusEl.hidden = false;
            }
            if (resultEl) resultEl.hidden = true;
            return;
        }

        if (submitBtn) submitBtn.disabled = true;
        if (statusEl) {
            statusEl.textContent = "Đang tra cứu...";
            statusEl.hidden = false;
        }
        if (resultEl) resultEl.hidden = true;

        try {
            const url = `${lookupUrl}?q=${encodeURIComponent(query)}&direction=${encodeURIComponent(direction)}`;
            const response = await fetch(url, { headers: { Accept: "application/json" }, credentials: "same-origin" });
            const data = await response.json();
            if (!response.ok) {
                throw new Error(data?.error || "Không thể tra cứu từ lúc này.");
            }

            if (wordEl) wordEl.textContent = data.query || query;
            if (phoneticEl) {
                phoneticEl.textContent = data.phonetic || "";
                phoneticEl.hidden = !data.phonetic;
            }
            currentAudioUrl = data.audioUrl || "";
            if (audioBtn) audioBtn.hidden = !currentAudioUrl;
            if (transEl) transEl.textContent = data.translation || "";

            if (altWrapEl && altsEl) {
                altsEl.replaceChildren();
                const alts = Array.isArray(data.alternativeTranslations) ? data.alternativeTranslations : [];
                alts.forEach(alt => {
                    const span = document.createElement("span");
                    span.className = "today-tag";
                    span.textContent = alt;
                    altsEl.appendChild(span);
                });
                altWrapEl.hidden = alts.length === 0;
            }

            if (groupsEl) {
                groupsEl.replaceChildren();
                const groups = Array.isArray(data.meaningGroups) ? data.meaningGroups : [];
                groups.forEach(group => {
                    const card = document.createElement("section");
                    card.className = "dict-pos-group";

                    const posTitle = document.createElement("h3");
                    posTitle.className = "dict-pos-title";
                    posTitle.textContent = group.partOfSpeechLabel || group.partOfSpeech;
                    card.appendChild(posTitle);

                    const defs = Array.isArray(group.definitions) ? group.definitions : [];
                    if (defs.length > 0) {
                        const list = document.createElement("ol");
                        list.className = "dict-def-list";
                        defs.forEach(def => {
                            const li = document.createElement("li");
                            const p = document.createElement("p");
                            p.textContent = def.definition;
                            li.appendChild(p);
                            if (def.example) {
                                const ex = document.createElement("small");
                                ex.className = "today-muted";
                                ex.textContent = `Ví dụ: "${def.example}"`;
                                li.appendChild(ex);
                            }
                            list.appendChild(li);
                        });
                        card.appendChild(list);
                    }

                    const syns = Array.isArray(group.synonyms) ? group.synonyms : [];
                    if (syns.length > 0) {
                        const synRow = document.createElement("div");
                        synRow.className = "dict-syn-row";
                        const label = document.createElement("span");
                        label.className = "dict-syn-label";
                        label.textContent = "Từ đồng nghĩa:";
                        const chips = document.createElement("div");
                        chips.className = "dict-chip-list";
                        syns.forEach(w => chips.appendChild(makeClickableWordChip(w)));
                        synRow.append(label, chips);
                        card.appendChild(synRow);
                    }

                    const ants = Array.isArray(group.antonyms) ? group.antonyms : [];
                    if (ants.length > 0) {
                        const antRow = document.createElement("div");
                        antRow.className = "dict-syn-row";
                        const label = document.createElement("span");
                        label.className = "dict-syn-label";
                        label.textContent = "Từ trái nghĩa:";
                        const chips = document.createElement("div");
                        chips.className = "dict-chip-list";
                        ants.forEach(w => chips.appendChild(makeClickableWordChip(w, "is-antonym")));
                        antRow.append(label, chips);
                        card.appendChild(antRow);
                    }

                    groupsEl.appendChild(card);
                });
                groupsEl.hidden = groups.length === 0;
            }

            if (sourceEl) sourceEl.textContent = data.source ? `Nguồn: ${data.source}` : "";
            if (statusEl) statusEl.hidden = true;
            if (resultEl) resultEl.hidden = false;

            if (data.translation) {
                addToHistory(data.query || query, data.translation, direction);
            }
        } catch (err) {
            if (statusEl) {
                statusEl.textContent = err instanceof Error ? err.message : "Tra từ thất bại. Vui lòng thử lại.";
                statusEl.hidden = false;
            }
            if (resultEl) resultEl.hidden = true;
        } finally {
            if (submitBtn) submitBtn.disabled = false;
        }
    };

    dirBtns.forEach(btn => btn.addEventListener("click", () => {
        setDirection(btn.dataset.dictDir);
        input?.focus();
    }));

    sampleBtns.forEach(btn => btn.addEventListener("click", () => {
        const sample = btn.dataset.dictSample || "";
        const sampleDir = btn.dataset.sampleDir || "en-vi";
        setDirection(sampleDir);
        if (input) input.value = sample;
        performLookup(sample);
    }));

    audioBtn?.addEventListener("click", () => {
        if (!currentAudioUrl) return;
        const audio = new Audio(currentAudioUrl);
        audio.play().catch(() => {});
    });

    clearHistoryBtn?.addEventListener("click", () => {
        localStorage.removeItem(historyKey);
        renderHistory();
    });

    form?.addEventListener("submit", event => {
        event.preventDefault();
        performLookup(input?.value || "");
    });

    renderHistory();
});
