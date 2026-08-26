import { Injectable, effect, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const STORAGE_KEY = 'pl-theme';

/** Dark mode flips one attribute on <html>; the token layer does everything
 *  else. No component ever learns which theme is active. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(attributeTheme() ?? storedTheme() ?? systemTheme());

  constructor() {
    effect(() => {
      const theme = this.theme();
      document.documentElement.setAttribute('data-theme', theme);
      try {
        localStorage.setItem(STORAGE_KEY, theme);
      } catch {
        // Private-browsing modes reject writes. A theme that does not persist
        // is a far smaller problem than a constructor that throws.
      }
    });
  }

  toggle(): void {
    this.theme.update(t => (t === 'dark' ? 'light' : 'dark'));
  }
}

/* The inline script in index.html has already resolved the theme before first
   paint. Reading it back rather than recomputing keeps the two in step. */
function attributeTheme(): Theme | null {
  const value = document.documentElement.getAttribute('data-theme');
  return value === 'light' || value === 'dark' ? value : null;
}

function storedTheme(): Theme | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === 'light' || value === 'dark' ? value : null;
  } catch {
    return null;
  }
}

// Guarded because matchMedia is absent under jsdom, where the unit tests run.
function systemTheme(): Theme {
  return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light';
}
