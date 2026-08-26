import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
    }).compileComponents();
  });

  it('creates the application shell', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders a router outlet for feature routes to mount into', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('router-outlet')).not.toBeNull();
  });

  it('publishes the active theme onto the document element', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    // Dark mode is delivered entirely by this attribute; the token layer does
    // the rest, so this is the whole contract between app and design system.
    expect(['light', 'dark']).toContain(document.documentElement.getAttribute('data-theme'));
  });
});
