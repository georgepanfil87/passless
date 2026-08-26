import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { AuthStore } from '../../core/auth.store';
import { Severity } from '../../core/models';

@Component({
  selector: 'pl-activity-log',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="border border-line bg-surface">
      <header class="flex flex-wrap items-center justify-between gap-4 border-b border-line-soft px-5 py-5 sm:px-6">
        <div>
          <h2 class="text-title font-semibold">Security activity</h2>
          <p class="mt-1 text-body text-muted">Last 30 days · newest first</p>
        </div>
        <div class="flex gap-2 font-mono text-meta" role="group" aria-label="Filter by severity">
          <button type="button" (click)="store.criticalOnly.set(false)"
            [attr.aria-pressed]="!store.criticalOnly()"
            [class]="filterClass(!store.criticalOnly())">ALL</button>
          <button type="button" (click)="store.criticalOnly.set(true)"
            [attr.aria-pressed]="store.criticalOnly()"
            [class]="filterClass(store.criticalOnly())">CRITICAL</button>
        </div>
      </header>

      <ol class="list-none p-0 m-0">
        @for (e of store.visibleEvents(); track e.id) {
          <li class="grid grid-cols-[22px_1fr] gap-x-4 gap-y-1 border-b border-line-soft px-5 py-4.5
                     last:border-b-0 sm:px-6 md:grid-cols-[22px_1fr_auto]">
            <!-- Severity is shape first, colour second, and the text label
                 beside it repeats the value: colour is never the only carrier. -->
            <span class="mt-1.5 block h-2.25 w-2.25" [class]="dotClass(e.severity)" aria-hidden="true"></span>
            <div class="flex flex-col gap-1.5">
              <div class="flex flex-wrap items-center gap-2.5">
                <span class="pl-eyebrow" [class.text-warn-text]="e.severity === 'critical'">{{ e.severity }}</span>
                <span class="text-body-lg font-medium">{{ e.title }}</span>
              </div>
              <p class="max-w-[52ch] text-body text-muted">{{ e.detail }}</p>
              <div class="flex flex-wrap gap-3.5 font-mono text-meta text-faint">
                @for (m of e.meta; track m) { <span>{{ m }}</span> }
              </div>
              <p class="font-mono text-caption text-muted md:hidden">{{ e.timeLabel }}</p>
            </div>
            <span class="hidden whitespace-nowrap font-mono text-caption text-muted md:block">{{ e.timeLabel }}</span>
          </li>
        }
      </ol>
    </section>
  `,
})
export class ActivityLogComponent {
  readonly store = inject(AuthStore);

  /* 'critical' is one of the four sanctioned uses of the warning colour. The
     other two severities are deliberately neutral so that the amber square
     stays rare enough to mean something. */
  dotClass(s: Severity): string {
    if (s === 'critical') return 'bg-warn';
    if (s === 'info') return 'bg-accent';
    return 'border-[1.5px] border-muted';
  }

  filterClass(active: boolean): string {
    return active
      ? 'border border-muted px-2.5 py-1.5 text-ink'
      : 'border border-line px-2.5 py-1.5 text-muted hover:border-muted';
  }
}
