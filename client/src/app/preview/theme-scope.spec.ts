import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ThemeScopeComponent } from './theme-scope.component';

@Component({
  imports: [ThemeScopeComponent],
  template: `
    <pl-theme-scope theme="light"><span class="probe">light</span></pl-theme-scope>
    <pl-theme-scope theme="dark"><span class="probe">dark</span></pl-theme-scope>
  `,
})
class Host {}

describe('ThemeScopeComponent', () => {
  it('opens independent theme scopes on one page', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const scopes = (fixture.nativeElement as HTMLElement).querySelectorAll('pl-theme-scope');

    // Both themes coexist in one DOM. This is the property the preview route
    // relies on, and it only holds because the token blocks key on a plain
    // [data-theme] attribute rather than :root[data-theme].
    expect(scopes.length).toBe(2);
    expect(scopes[0].getAttribute('data-theme')).toBe('light');
    expect(scopes[1].getAttribute('data-theme')).toBe('dark');
  });
});
