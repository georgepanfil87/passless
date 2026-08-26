import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Theme } from '../../design/core/theme.service';

/**
 * Opens a theme scope around whatever is projected into it.
 *
 * This works only because the dark and light token blocks are keyed on a plain
 * `[data-theme]` attribute selector rather than `:root[data-theme]`, and because
 * the Tailwind mapping uses `@theme inline` so utilities emit `var(--pl-…)`
 * rather than a value pinned at the root. Both themes can therefore be painted
 * on one page without iframes, and no component below here can tell which one
 * it is in.
 */
@Component({
  selector: 'pl-theme-scope',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-theme]': 'theme()',
    class: 'pl-theme-scope block',
  },
  template: '<ng-content />',
})
export class ThemeScopeComponent {
  readonly theme = input.required<Theme>();
}
