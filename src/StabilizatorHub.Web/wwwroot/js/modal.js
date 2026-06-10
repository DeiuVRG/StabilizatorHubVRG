// Minimal promise-based modal used for confirmations and single-input prompts.

const backdrop = () => document.getElementById('modal');

export function confirmDialog({ title, text, confirmText = 'Confirm', danger = false }) {
  return openModal({ title, text, confirmText, danger, withInput: false });
}

export function promptDialog({ title, text, confirmText = 'Save', initialValue = '' }) {
  return openModal({ title, text, confirmText, danger: false, withInput: true, initialValue });
}

function openModal({ title, text, confirmText, danger, withInput, initialValue = '' }) {
  const host = backdrop();
  const titleEl = document.getElementById('modal-title');
  const textEl = document.getElementById('modal-text');
  const inputWrap = document.getElementById('modal-input-wrap');
  const input = document.getElementById('modal-input');
  const confirmBtn = document.getElementById('modal-confirm');
  const cancelBtn = document.getElementById('modal-cancel');

  titleEl.textContent = title;
  textEl.textContent = text ?? '';
  confirmBtn.textContent = confirmText;
  confirmBtn.className = danger ? 'btn danger' : 'btn primary';

  inputWrap.classList.toggle('hidden', !withInput);
  input.value = initialValue;

  host.classList.add('visible');
  if (withInput) input.focus();

  return new Promise(resolve => {
    const close = result => {
      host.classList.remove('visible');
      confirmBtn.removeEventListener('click', onConfirm);
      cancelBtn.removeEventListener('click', onCancel);
      host.removeEventListener('click', onBackdrop);
      document.removeEventListener('keydown', onKey);
      resolve(result);
    };

    const onConfirm = () => close(withInput ? input.value.trim() : true);
    const onCancel = () => close(withInput ? null : false);
    const onBackdrop = event => { if (event.target === host) onCancel(); };
    const onKey = event => {
      if (event.key === 'Escape') onCancel();
      if (event.key === 'Enter' && withInput) onConfirm();
    };

    confirmBtn.addEventListener('click', onConfirm);
    cancelBtn.addEventListener('click', onCancel);
    host.addEventListener('click', onBackdrop);
    document.addEventListener('keydown', onKey);
  });
}
