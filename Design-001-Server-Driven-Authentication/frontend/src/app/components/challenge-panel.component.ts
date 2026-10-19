import { ChangeDetectionStrategy, Component, OnDestroy, computed, input, output, signal } from '@angular/core';
import { Challenge, ChallengeResponse } from '../models/auth.models';

/**
 * Challenge types this client knows how to present with an icon.
 * This is NOT policy: it says nothing about priority or eligibility, only
 * which server-provided types the UI has a dedicated look for.
 */
const KNOWN_ICONS: Record<string, string> = {
  Password: '🔑',
  EmailOtp: '✉️',
  Totp: '📱',
};

@Component({
  selector: 'app-challenge-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (response().selectedChallenge; as selected) {
      <section class="card" aria-live="polite">
        <p class="badge">Server-selected challenge</p>
        <h2 class="challenge-title">
          <span aria-hidden="true">{{ icon(selected) }}</span>
          <span data-testid="selected-name">{{ selected.displayName }}</span>
        </h2>
        <p data-testid="selected-type" class="type">Type: {{ selected.type }}</p>
        <p data-testid="selected-description">{{ selected.description }}</p>

        @if (!isSupported(selected)) {
          <p class="warning" role="alert" data-testid="unsupported-selected">
            This client does not recognize the "{{ selected.type }}" method. The server chose it, but
            it can't be presented here. Try another way, if one is offered.
          </p>
        }

        @if (expired()) {
          <p class="warning" role="alert" data-testid="expired">
            This flow has expired. Start a new one.
          </p>
          <button type="button" class="primary" (click)="restart.emit()">Start new flow</button>
        } @else {
          <p class="meta" data-testid="expiry">
            Flow expires at {{ expiresAtLabel() }} ({{ remainingLabel() }} left)
          </p>
        }

        @if (response().alternatives.length > 0) {
          <h3>Try another way</h3>
          <ul class="alternatives" data-testid="alternatives">
            @for (alt of response().alternatives; track alt.type) {
              <li>
                <button
                  type="button"
                  class="alt"
                  [disabled]="busy() || expired() || !isSupported(alt)"
                  (click)="alternativeSelected.emit(alt.type)"
                >
                  <span aria-hidden="true">{{ icon(alt) }}</span>
                  <span class="alt-text">
                    <strong>{{ alt.displayName }}</strong>
                    <small>
                      {{ alt.description }}
                      @if (!isSupported(alt)) {
                        (not supported by this client)
                      }
                    </small>
                  </span>
                </button>
              </li>
            }
          </ul>
        } @else {
          <p class="meta" data-testid="no-alternatives">The server offered no alternative methods.</p>
        }

        <button type="button" class="link" (click)="restart.emit()">Start over with another account</button>
      </section>
    } @else {
      <section class="card" data-testid="no-methods">
        <h2>No authentication method available</h2>
        <p>The server found no eligible authentication method for this account, so it started no flow.</p>
        <button type="button" class="primary" (click)="restart.emit()">Try another account</button>
      </section>
    }
  `,
})
export class ChallengePanelComponent implements OnDestroy {
  readonly response = input.required<ChallengeResponse>();
  readonly busy = input(false);
  readonly alternativeSelected = output<string>();
  readonly restart = output<void>();

  private readonly now = signal(Date.now());
  private readonly timer = setInterval(() => this.now.set(Date.now()), 1000);

  private readonly expiresAtMs = computed(() => {
    const at = this.response().expiresAt;
    return at ? Date.parse(at) : NaN;
  });

  protected readonly expired = computed(
    () => !Number.isNaN(this.expiresAtMs()) && this.expiresAtMs() <= this.now(),
  );
  protected readonly expiresAtLabel = computed(() =>
    Number.isNaN(this.expiresAtMs()) ? 'unknown' : new Date(this.expiresAtMs()).toLocaleTimeString(),
  );
  protected readonly remainingLabel = computed(() => {
    if (Number.isNaN(this.expiresAtMs())) return 'unknown time';
    const s = Math.max(0, Math.floor((this.expiresAtMs() - this.now()) / 1000));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  protected isSupported(c: Challenge): boolean {
    return c.type in KNOWN_ICONS;
  }

  protected icon(c: Challenge): string {
    return KNOWN_ICONS[c.type] ?? '❓';
  }
}
