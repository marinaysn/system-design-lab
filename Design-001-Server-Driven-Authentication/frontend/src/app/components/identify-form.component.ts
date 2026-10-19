import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

export interface IdentifySubmission {
  identifier: string;
  region?: string;
}

/** Demo shortcuts only. They fill the input; they carry no policy information. */
const DEMO_ACCOUNTS = [
  { label: 'A', identifier: 'account.a@example.test' },
  { label: 'B', identifier: 'account.b@example.test' },
  { label: 'C', identifier: 'account.c@example.test' },
  { label: 'D', identifier: 'account.d@example.test' },
];

@Component({
  selector: 'app-identify-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form (submit)="submit($event)" class="card" novalidate>
      <h2>Identify account</h2>

      <div class="demo-accounts" role="group" aria-label="Demo accounts">
        <span class="hint">Demo accounts:</span>
        @for (a of demoAccounts; track a.label) {
          <button type="button" class="chip" [title]="a.identifier" (click)="identifier.set(a.identifier)">
            {{ a.label }}
          </button>
        }
      </div>

      <label for="identifier">Identifier</label>
      <input
        id="identifier"
        name="identifier"
        type="text"
        autocomplete="off"
        placeholder="account.a@example.test"
        [value]="identifier()"
        (input)="identifier.set($any($event.target).value)"
      />

      <label for="region">Region <span class="hint">(optional, e.g. EU)</span></label>
      <input
        id="region"
        name="region"
        type="text"
        autocomplete="off"
        [value]="region()"
        (input)="region.set($any($event.target).value)"
      />

      <button type="submit" class="primary" [disabled]="busy()">
        {{ busy() ? 'Contacting server…' : 'Continue' }}
      </button>
    </form>
  `,
})
export class IdentifyFormComponent {
  readonly busy = input(false);
  readonly submitted = output<IdentifySubmission>();

  protected readonly demoAccounts = DEMO_ACCOUNTS;
  protected readonly identifier = signal('');
  protected readonly region = signal('');

  protected submit(event: Event): void {
    event.preventDefault();
    const region = this.region().trim();
    // Validation of the values is the server's job; an empty identifier is still sent
    // so the server's error response is what the user sees.
    this.submitted.emit({
      identifier: this.identifier().trim(),
      ...(region ? { region } : {}),
    });
  }
}
