<script lang="ts">
  import svelteLogo from './assets/svelte.svg';
  import viteLogo from '/vite.svg';
  import Counter from './lib/Counter.svelte';
  import { onMount } from 'svelte';
  import {
    filesystem,
    os,
    app,
    window,
    computer,
    type TrayOptions,
    events,
  } from '@neutralinojs/lib';

  onMount(async () => {
    const screens = await computer.getDisplays();
    const screen = screens[0].resolution;
    const windowSize = await window.getSize();

    const x = screen.width - windowSize.width!;
    const y = screen.height - windowSize.height! - 40;

    await window.move(x, y);

    const tray: TrayOptions = {
      icon: '/resources/trayIcon.png',
      menuItems: [{ id: 'open', text: 'Open' }, { text: '-' }, { id: 'quit', text: 'Quit' }],
    };

    await os.setTray(tray);

    events.on('trayMenuItemClicked', async (event) => {
      switch (event.detail.id) {
        case 'open':
          await window.show();
          await window.focus();
          break;

        case 'quit':
          app.exit();
          break;
      }
    });

    filesystem
      .readDirectory('./')
      .then((data) => {
        console.log(data);
      })
      .catch((err) => {
        console.log(err);
      });
  });
</script>

<main>
  <div>
    <a href="https://vite.dev" target="_blank" rel="noreferrer">
      <img src={viteLogo} class="logo" alt="Vite Logo" />
    </a>
    <a href="https://svelte.dev" target="_blank" rel="noreferrer">
      <img src={svelteLogo} class="logo svelte" alt="Svelte Logo" />
    </a>
  </div>
  <h1>Vite + Svelte</h1>

  <div class="card">
    <Counter />
  </div>

  <p>
    Check out <a href="https://github.com/sveltejs/kit#readme" target="_blank" rel="noreferrer"
      >SvelteKit</a
    >, the official Svelte app framework powered by Vite!
  </p>

  <p class="read-the-docs">Click on the Vite and Svelte logos to learn more</p>
</main>

<style>
  .logo {
    height: 6em;
    padding: 1.5em;
    will-change: filter;
    transition: filter 300ms;
  }
  .logo:hover {
    filter: drop-shadow(0 0 2em #646cffaa);
  }
  .logo.svelte:hover {
    filter: drop-shadow(0 0 2em #ff3e00aa);
  }
  .read-the-docs {
    color: #888;
  }
</style>
