import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ChallengeResponse, ApiError } from './models/auth.models';
import { AuthApiService } from './services/auth-api.service';
import { ChallengePanelComponent } from './components/challenge-panel.component';
import { IdentifyFormComponent, IdentifySubmission } from './components/identify-form.component';

@Component({
  selector: 'app-root',
  imports: [IdentifyFormComponent, ChallengePanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly api = inject(AuthApiService);

  protected readonly response = signal<ChallengeResponse | null>(null);
  protected readonly error = signal<ApiError | null>(null);
  protected readonly busy = signal(false);

  protected identify(submission: IdentifySubmission): void {
    this.begin();
    this.api.identify(submission).subscribe({
      next: (r) => this.succeed(r),
      error: (e: ApiError) => this.fail(e),
    });
  }

  protected selectAlternative(type: string): void {
    const flowId = this.response()?.flowId;
    if (!flowId) return;
    this.begin();
    this.api.selectChallenge(flowId, type).subscribe({
      next: (r) => this.succeed(r),
      error: (e: ApiError) => this.fail(e),
    });
  }

  protected restart(): void {
    this.response.set(null);
    this.error.set(null);
  }

  private begin(): void {
    this.busy.set(true);
    this.error.set(null);
  }

  private succeed(r: ChallengeResponse): void {
    this.response.set(r);
    this.busy.set(false);
  }

  private fail(e: ApiError): void {
    this.busy.set(false);
    this.error.set(e);
    // The flow is gone or expired server-side: do not keep showing a dead challenge.
    if (e.restartRequired) this.response.set(null);
  }
}
