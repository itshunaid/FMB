function switchTab(tabName, element) {
    document.querySelectorAll('nav a').forEach(a => a.classList.remove('on'));
    if (element) element.classList.add('on');
    fetch('/Home/GetTab?name=' + tabName)
        .then(res => res.text())
        .then(html => { document.getElementById('mn').innerHTML = html; });
}

function showToast(msg) {
    let t = document.getElementById('ts');
    if (!t) {
        t = document.createElement('div');
        t.id = 'ts';
        t.className = 'toast-notice';
        t.setAttribute('role', 'status');
        document.body.appendChild(t);
    }
    t.textContent = msg;
    t.style.display = 'block';
    setTimeout(() => { t.style.display = 'none'; }, 3500);
}

function setLoginRole(role) {
    const roleInput = document.getElementById('loginAs');
    const idLabel = document.getElementById('loginIdLabel');
    const idInput = document.getElementById('ItsId');
    if (!roleInput || !idLabel || !idInput) return;
    roleInput.value = role;
    const household = role === 'Household';
    idLabel.textContent = household ? 'HOF ITS ID or Thali number' : 'User ITS ID';
    idInput.placeholder = household ? idInput.dataset.householdPlaceholder : idInput.dataset.staffPlaceholder;
    document.querySelectorAll('[data-login-role]').forEach(button => {
        const selected = button.dataset.loginRole === role;
        button.classList.toggle('selected', selected);
        button.setAttribute('aria-pressed', selected ? 'true' : 'false');
    });
    document.querySelectorAll('[data-login-demo]').forEach(demo => {
        demo.hidden = demo.dataset.loginDemo !== role;
    });
}

document.addEventListener('click', event => {
    const roleButton = event.target.closest('[data-login-role]');
    if (roleButton) setLoginRole(roleButton.dataset.loginRole);
});

if (document.getElementById('loginAs')) {
    setLoginRole(document.getElementById('loginAs').value || 'Household');
}

function quickScan(tagId) {
    const tagInput = document.getElementById('tagInput');
    if (!tagInput) return;
    tagInput.value = tagId;
    triggerScan();
}

async function triggerScan() {
    const tagInput = document.getElementById('tagInput');
    if (!tagInput) return;
    const tagId = tagInput.value.trim();
    if (!tagId) {
        showScanResult('Enter an RFID tag ID.');
        return;
    }

    const token = document.querySelector('#scanTokenForm input[name="__RequestVerificationToken"]')?.value;
    try {
        const response = await fetch('/Home/SimulateScan', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token || ''
        },
        body: JSON.stringify({ tagId: tagId })
        });
        const data = await response.json();
        if (!response.ok) throw new Error(data.detail || data.message || 'Scan request failed.');
        showScanResult((data.householdName ? data.householdName + ': ' : '') + data.message);
    } catch (error) {
        showScanResult(error.message || 'Scan request failed.');
    }
}

function showScanResult(message) {
    const result = document.getElementById('scanResult');
    if (result) {
        result.textContent = message;
        result.hidden = false;
        return;
    }
    showToast(message);
}

document.addEventListener('click', event => {
    const button = event.target.closest('.quick-scan');
    if (button) quickScan(button.dataset.tagId);
});

function setTheme(theme) {
    document.documentElement.dataset.theme = theme;
    localStorage.setItem('fmb-theme', theme);
    document.querySelectorAll('[data-theme-toggle]').forEach(button => {
        button.textContent = theme === 'dark' ? 'Light mode' : 'Dark mode';
    });
}

setTheme(localStorage.getItem('fmb-theme') || 'light');
document.addEventListener('click', event => {
    if (event.target.closest('[data-theme-toggle]')) {
        setTheme(document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark');
    }
});

document.addEventListener('click', event => {
    const opener = event.target.closest('[data-modal-open]');
    if (opener) {
        const modal = document.getElementById(opener.dataset.modalOpen);
        if (modal) {
            modal.hidden = false;
            modal.querySelector('input, select, textarea, button')?.focus();
        }
        return;
    }

    if (event.target.closest('[data-modal-close]')) {
        const modal = event.target.closest('.modal-backdrop');
        if (modal) modal.hidden = true;
        return;
    }

    const backdrop = event.target.closest('.modal-backdrop');
    if (backdrop && event.target === backdrop) backdrop.hidden = true;

    const feedbackButton = event.target.closest('[data-feedback-view]');
    if (feedbackButton) {
        const modal = document.getElementById('feedback-view-modal');
        if (!modal) return;
        const values = {
            member: feedbackButton.dataset.member,
            its: feedbackButton.dataset.its,
            date: `${feedbackButton.dataset.date} · ${feedbackButton.dataset.menu}`,
            zone: feedbackButton.dataset.zone,
            rating: `${feedbackButton.dataset.rating} / 5`,
            quantity: feedbackButton.dataset.quantity,
            quality: feedbackButton.dataset.quality,
            comment: feedbackButton.dataset.comment || '—'
        };
        Object.entries(values).forEach(([key, value]) => {
            const target = modal.querySelector(`[data-view-${key}]`);
            if (target) target.textContent = value;
        });
        modal.hidden = false;
    }

    const menuButton = event.target.closest('[data-menu-view]');
    if (menuButton) {
        const modal = document.getElementById('menu-view-modal');
        if (modal) {
            modal.querySelector('[data-menu-date]').textContent = menuButton.dataset.date;
            modal.querySelector('[data-menu-name]').textContent = menuButton.dataset.menu;
            modal.hidden = false;
        }
    }

    const expenseButton = event.target.closest('[data-expense-view]');
    if (expenseButton) {
        const modal = document.getElementById('expense-view-modal');
        if (modal) {
            const fields = {
                date: 'date', menu: 'menu', thalis: 'thalis', total: 'total',
                'per-thali': 'perThali', groceries: 'groceries', meat: 'meat',
                gas: 'gas', labour: 'labour', packaging: 'packaging', other: 'other', notes: 'notes'
            };
            Object.entries(fields).forEach(([target, source]) => {
                const element = modal.querySelector(`[data-expense-${target}]`);
                if (element) element.textContent = expenseButton.dataset[source] || '—';
            });
            modal.hidden = false;
        }
    }

    const userButton = event.target.closest('[data-user-view]');
    if (userButton) {
        const modal = document.getElementById('user-view-modal');
        if (modal) {
            const fields = ['its', 'name', 'role', 'phone', 'status', 'lastLogin'];
            fields.forEach(field => {
                const target = modal.querySelector(`[data-user-${field.replace(/[A-Z]/g, letter => `-${letter.toLowerCase()}`)}]`);
                if (target) target.textContent = userButton.dataset[field] || '—';
            });
            modal.hidden = false;
        }
    }

    if (event.target.closest('[data-copy-link]')) {
        const input = document.getElementById('feedback-link');
        if (input && navigator.clipboard) {
            navigator.clipboard.writeText(input.value)
                .then(() => showToast('Feedback link copied.'))
                .catch(() => showToast('Could not copy the link. Copy it from the field instead.'));
        }
    }
});

document.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
        document.querySelectorAll('.modal-backdrop:not([hidden])').forEach(modal => { modal.hidden = true; });
    }
});
