/* ══════════════════════════════════════════════════════════
   Xvirus – Thank-you page: claims the license for a Polar checkout
   Polar redirects here with ?checkout_id=<id>
   ══════════════════════════════════════════════════════════ */

(() => {
  const POLL_DELAY_MS = 3000;
  const MAX_POLLS = 20; // ~1 minute while Polar confirms the payment
  const MAX_NETWORK_RETRIES = 3;

  const $ = (id) => document.getElementById(id);

  function show(state) {
    document.querySelectorAll('.license-card [data-state]').forEach((el) => {
      el.hidden = el.dataset.state !== state;
    });
  }

  function showError(message) {
    $('error-text').textContent = message;
    show('error');
  }

  function copyButton(key) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'btn btn--outline btn--sm license-keys__copy';
    btn.textContent = 'Copy';
    btn.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(key);
        btn.textContent = 'Copied';
      } catch {
        btn.textContent = 'Select & copy';
      }
      setTimeout(() => (btn.textContent = 'Copy'), 2000);
    });
    return btn;
  }

  function showKeys(data) {
    const keys = Array.isArray(data.keys) ? data.keys : [];
    const plural = keys.length > 1;

    $('success-title').textContent = `Your ${data.product || 'Xvirus Pro'} license key${plural ? 's' : ''}:`;

    const list = $('license-keys');
    list.replaceChildren();
    keys.forEach((key) => {
      const li = document.createElement('li');
      const code = document.createElement('code');
      code.textContent = key;
      li.append(code, copyButton(key));
      list.append(li);
    });

    $('success-note').textContent =
      data.status === 'email_failed'
        ? "We couldn't send the confirmation email right now. Please save your key now. Reloading this page will try the email again."
        : `We also emailed ${plural ? 'them' : 'it'} to ${data.email || 'the address you used at checkout'}.`;

    $('success-howto').textContent = plural
      ? 'Each key activates one PC. Open Xvirus on each computer, open the Pro activation window and enter one key.'
      : 'Open Xvirus, open the Pro activation window and enter the key above.';

    show('success');
  }

  async function claim(checkoutId, poll = 1, networkRetry = 0) {
    let res;
    let data = {};
    try {
      res = await fetch('/license/claim', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ checkoutId }),
      });
      data = await res.json().catch(() => ({}));
    } catch {
      if (networkRetry < MAX_NETWORK_RETRIES) {
        setTimeout(() => claim(checkoutId, poll, networkRetry + 1), POLL_DELAY_MS);
        return;
      }
      showError('We could not reach our server. Please check your connection and reload this page.');
      return;
    }

    switch (data.status) {
      case 'processed':
      case 'email_failed':
        showKeys(data);
        return;
      case 'already_processed':
        show('already');
        return;
      case 'pending':
        if (poll < MAX_POLLS) {
          $('loading-text').textContent = 'Waiting for your payment to be confirmed…';
          setTimeout(() => claim(checkoutId, poll + 1, 0), POLL_DELAY_MS);
        } else {
          show('slow');
        }
        return;
      default:
        showError(data.error || 'Something went wrong. Please reload this page in a minute.');
    }
  }

  document.addEventListener('DOMContentLoaded', () => {
    const reload = $('reload-link');
    if (reload) reload.href = window.location.href;

    const checkoutId = new URLSearchParams(window.location.search).get('checkout_id');
    if (!checkoutId) {
      showError('This page is missing the purchase reference.');
      return;
    }
    claim(checkoutId);
  });
})();
