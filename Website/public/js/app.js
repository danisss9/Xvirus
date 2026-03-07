/* ══════════════════════════════════════════════════════════
   Xvirus – Client-side JavaScript
   ══════════════════════════════════════════════════════════ */

document.addEventListener('DOMContentLoaded', () => {
  /* ── Mobile nav toggle ────────────────────────────────── */
  const toggle = document.querySelector('.nav__toggle');
  const links = document.querySelector('.nav__links');

  if (toggle && links) {
    toggle.addEventListener('click', () => {
      toggle.classList.toggle('open');
      links.classList.toggle('open');
    });

    // Close mobile menu when a link is clicked
    links.querySelectorAll('a').forEach((a) => {
      a.addEventListener('click', () => {
        toggle.classList.remove('open');
        links.classList.remove('open');
      });
    });
  }

  /* ── Navbar scroll effect ─────────────────────────────── */
  const nav = document.querySelector('.nav');
  if (nav) {
    const onScroll = () => {
      nav.classList.toggle('scrolled', window.scrollY > 40);
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }

  /* ── Reveal-on-scroll (IntersectionObserver) ──────────── */
  const reveals = document.querySelectorAll('.reveal');
  if (reveals.length && 'IntersectionObserver' in window) {
    const observer = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add('visible');
            observer.unobserve(entry.target);
          }
        });
      },
      { threshold: 0.12, rootMargin: '0px 0px -40px 0px' },
    );
    reveals.forEach((el) => observer.observe(el));
  } else {
    // Fallback: just show everything
    reveals.forEach((el) => el.classList.add('visible'));
  }

  /* ── Success overlay animation ─────────────────────────── */
  function showSuccessOverlay() {
    // Remove any existing overlay
    const existing = document.querySelector('.success-overlay');
    if (existing) existing.remove();

    const overlay = document.createElement('div');
    overlay.className = 'success-overlay';
    overlay.innerHTML = `
      <div class="success-overlay__content">
        <svg class="success-checkmark" viewBox="0 0 80 80">
          <circle class="success-checkmark__circle" cx="40" cy="40" r="36" fill="none" stroke-width="3"/>
          <path class="success-checkmark__tick" d="M24 42 L35 53 L56 28" fill="none" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
        <p class="success-overlay__text">Sent successfully!</p>
      </div>
      <div class="success-particles"></div>
    `;
    document.body.appendChild(overlay);

    // Spawn confetti particles
    const particleContainer = overlay.querySelector('.success-particles');
    const colors = ['#00e676', '#00c853', '#69f0ae', '#b9f6ca', '#ffffff', '#76ff03'];
    for (let i = 0; i < 40; i++) {
      const p = document.createElement('span');
      p.className = 'success-particle';
      p.style.setProperty('--x', `${(Math.random() - 0.5) * 360}px`);
      p.style.setProperty('--y', `${(Math.random() - 0.5) * 360}px`);
      p.style.setProperty('--r', `${Math.random() * 720 - 360}deg`);
      p.style.setProperty('--delay', `${Math.random() * 0.3}s`);
      p.style.setProperty('--size', `${Math.random() * 6 + 3}px`);
      p.style.background = colors[Math.floor(Math.random() * colors.length)];
      particleContainer.appendChild(p);
    }

    // Auto-dismiss after 2.5s
    setTimeout(() => {
      overlay.classList.add('fade-out');
      overlay.addEventListener('animationend', () => overlay.remove());
    }, 2500);

    // Also allow click to dismiss
    overlay.addEventListener('click', () => {
      overlay.classList.add('fade-out');
      overlay.addEventListener('animationend', () => overlay.remove());
    });
  }

  /* ── Show form status helper ──────────────────────────── */
  function showFormStatus(statusEl, message, type) {
    statusEl.textContent = message;
    statusEl.className = `form-status ${type}`;
    statusEl.style.display = '';
  }

  /* ── Contact form handler ─────────────────────────────── */
  const contactForm = document.getElementById('contact-form');
  if (contactForm) {
    contactForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const btn = contactForm.querySelector('.btn');
      const status = document.getElementById('contact-status');

      btn.classList.add('loading');
      status.className = 'form-status';
      status.style.display = 'none';

      const data = {
        name: contactForm.name.value.trim(),
        email: contactForm.email.value.trim(),
        subject: contactForm.subject.value.trim(),
        message: contactForm.message.value.trim(),
      };

      // Try to get ALTCHA payload if widget is present
      const altchaInput = contactForm.querySelector('input[name="altcha"]');
      if (altchaInput) data.altcha = altchaInput.value;

      try {
        const res = await fetch('/contact', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(data),
        });
        const json = await res.json();

        if (res.ok) {
          showFormStatus(status, 'Message sent successfully!', 'success');
          contactForm.reset();
          showSuccessOverlay();
        } else {
          showFormStatus(status, json.error || 'Something went wrong.', 'error');
        }
      } catch {
        showFormStatus(status, 'Network error. Please try again.', 'error');
      } finally {
        btn.classList.remove('loading');
      }
    });
  }

  /* ── Submit form handler ──────────────────────────────── */
  const submitForm = document.getElementById('submit-form');
  if (submitForm) {
    const kindSelect = submitForm.querySelector('#kind');
    const fileGroup = document.getElementById('file-group');
    const urlGroup = document.getElementById('url-group');
    const fileInput = submitForm.querySelector('#file');
    const urlInput = submitForm.querySelector('#url');

    // Toggle file / URL fields based on kind
    if (kindSelect) {
      kindSelect.addEventListener('change', () => {
        const v = kindSelect.value;
        fileGroup.style.display = v === 'file' ? '' : 'none';
        urlGroup.style.display = v === 'url' ? '' : 'none';
        if (v === 'file') {
          urlInput.value = '';
          urlInput.removeAttribute('required');
          fileInput.setAttribute('required', '');
        }
        if (v === 'url') {
          fileInput.value = '';
          fileInput.removeAttribute('required');
          urlInput.setAttribute('required', '');
        }
      });
    }

    submitForm.addEventListener('submit', async (e) => {
      e.preventDefault();
      const btn = submitForm.querySelector('.btn');
      const status = document.getElementById('submit-status');

      btn.classList.add('loading');
      status.className = 'form-status';
      status.style.display = 'none';

      const kind = submitForm.kind.value;

      // Client-side file validation
      if (kind === 'file' && fileInput.files.length) {
        const f = fileInput.files[0];
        if (!f.name.toLowerCase().endsWith('.zip')) {
          showFormStatus(status, 'Only .zip files are accepted.', 'error');
          btn.classList.remove('loading');
          return;
        }
        if (f.size > 20 * 1024 * 1024) {
          showFormStatus(status, 'File must be no larger than 20 MB.', 'error');
          btn.classList.remove('loading');
          return;
        }
      }

      // Build FormData (supports file upload)
      const fd = new FormData();
      fd.append('name', submitForm.name.value.trim());
      fd.append('email', submitForm.email.value.trim());
      fd.append('rating', submitForm.rating.value);
      fd.append('kind', kind);

      if (kind === 'file' && fileInput.files.length) {
        fd.append('file', fileInput.files[0]);
      }
      if (kind === 'url') {
        fd.append('url', urlInput.value.trim());
      }

      const altchaInput = submitForm.querySelector('input[name="altcha"]');
      if (altchaInput) fd.append('altcha', altchaInput.value);

      try {
        const res = await fetch('/submit', {
          method: 'POST',
          body: fd,
        });
        const json = await res.json();

        if (res.ok) {
          showFormStatus(status, 'Submission sent successfully!', 'success');
          submitForm.reset();
          fileGroup.style.display = 'none';
          urlGroup.style.display = 'none';
          showSuccessOverlay();
        } else {
          showFormStatus(status, json.error || 'Something went wrong.', 'error');
        }
      } catch {
        showFormStatus(status, 'Network error. Please try again.', 'error');
      } finally {
        btn.classList.remove('loading');
      }
    });
  }

  /* ── Home link: always scroll to top ────────────────────── */
  document.querySelectorAll('.nav__links a[href="/"]').forEach((a) => {
    a.addEventListener('click', (e) => {
      e.preventDefault();
      window.scrollTo({ top: 0, behavior: 'smooth' });
    });
  });

  /* ── Smooth scroll for same-page hash links ───────────── */
  document.querySelectorAll('a[href^="#"]').forEach((anchor) => {
    anchor.addEventListener('click', (e) => {
      const target = document.querySelector(anchor.getAttribute('href'));
      if (target) {
        e.preventDefault();
        target.scrollIntoView({ behavior: 'smooth' });
      }
    });
  });

  /* ── Scroll-spy: highlight active nav link ────────────── */
  const navLinks = document.querySelectorAll('.nav__links a[href^="#"]');
  const homeLink = document.querySelector('.nav__links a[href="/"]');
  const sections = [];

  navLinks.forEach((link) => {
    const id = link.getAttribute('href').slice(1);
    const sec = document.getElementById(id);
    if (sec) sections.push({ id, el: sec, link });
  });

  if (sections.length) {
    const setActive = (activeLink) => {
      if (homeLink) homeLink.classList.remove('active');
      navLinks.forEach((l) => l.classList.remove('active'));
      if (activeLink) activeLink.classList.add('active');
    };

    const onScrollSpy = () => {
      const scrollY = window.scrollY + 120; // offset for fixed nav

      // If scrolled to the bottom of the page, highlight the last section link
      const atBottom =
        window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2;
      if (atBottom && sections.length) {
        setActive(sections[sections.length - 1].link);
        return;
      }

      // If near top, highlight Home
      if (sections.length && scrollY < sections[0].el.offsetTop) {
        setActive(homeLink);
        return;
      }

      // Find the last section whose top we've scrolled past
      let current = null;
      for (const s of sections) {
        if (scrollY >= s.el.offsetTop) current = s;
      }
      setActive(current ? current.link : homeLink);
    };

    window.addEventListener('scroll', onScrollSpy, { passive: true });
    onScrollSpy(); // run once on load
  }
});
