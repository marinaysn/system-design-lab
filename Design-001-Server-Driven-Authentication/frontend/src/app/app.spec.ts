import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { ChallengeResponse } from './models/auth.models';

const future = () => new Date(Date.now() + 10 * 60_000).toISOString();
const past = () => new Date(Date.now() - 60_000).toISOString();

function response(over: Partial<ChallengeResponse> = {}): ChallengeResponse {
  return {
    flowId: 'flow-1',
    expiresAt: future(),
    selectedChallenge: { type: 'EmailOtp', displayName: 'Email code', description: 'Enter the emailed code.' },
    alternatives: [
      { type: 'Totp', displayName: 'Authenticator app', description: 'Use your app.' },
      { type: 'Password', displayName: 'Password', description: 'Enter your password.' },
    ],
    notice: 'SIMULATION: test',
    ...over,
  };
}

describe('App (server-driven rendering)', () => {
  let http: HttpTestingController;
  let root: HTMLElement;
  let fixture: ReturnType<typeof TestBed.createComponent<App>>;

  const q = (sel: string) => root.querySelector(sel) as HTMLElement | null;
  const text = (sel: string) => q(sel)?.textContent?.replace(/\s+/g, ' ').trim();

  async function identify(identifier = 'account.a@example.test', region = '') {
    const id = q('#identifier') as HTMLInputElement;
    id.value = identifier;
    id.dispatchEvent(new Event('input'));
    if (region) {
      const r = q('#region') as HTMLInputElement;
      r.value = region;
      r.dispatchEvent(new Event('input'));
    }
    (q('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  async function flush(req: ReturnType<HttpTestingController['expectOne']>, body: unknown) {
    req.flush(body as object);
    await fixture.whenStable();
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(App);
    root = fixture.nativeElement;
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  it('always shows the simulation banner', () => {
    expect(text('[data-testid=sim-banner]')).toContain('SIMULATION');
  });

  it('posts the identifier (and optional region) to /api/auth/identify', async () => {
    await identify('account.a@example.test', 'EU');
    const req = http.expectOne('/api/auth/identify');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ identifier: 'account.a@example.test', region: 'EU' });
    await flush(req, response());
  });

  it('renders the selected challenge returned by the API, whatever it is', async () => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), response());
    expect(text('[data-testid=selected-name]')).toBe('Email code');
    expect(text('[data-testid=selected-type]')).toContain('EmailOtp');
    expect(text('[data-testid=selected-description]')).toBe('Enter the emailed code.');
    expect(text('[data-testid=expiry]')).toContain('expires');
    expect(text('[data-testid=server-notice]')).toContain('SIMULATION');
  });

  it('renders alternatives in the server-returned order', async () => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), response());
    const names = Array.from(root.querySelectorAll('[data-testid=alternatives] strong')).map((e) => e.textContent);
    expect(names).toEqual(['Authenticator app', 'Password']);
  });

  it('selecting an alternative calls the select endpoint and renders the server response', async () => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), response());

    (root.querySelectorAll('[data-testid=alternatives] button')[1] as HTMLButtonElement).click();
    const req = http.expectOne('/api/auth/flows/flow-1/select');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ authenticationType: 'Password' });

    await flush(
      req,
      response({
        selectedChallenge: { type: 'Password', displayName: 'Password', description: 'Enter your password.' },
        alternatives: [{ type: 'EmailOtp', displayName: 'Email code', description: 'Enter the emailed code.' }],
      }),
    );
    expect(text('[data-testid=selected-name]')).toBe('Password');
    const names = Array.from(root.querySelectorAll('[data-testid=alternatives] strong')).map((e) => e.textContent);
    expect(names).toEqual(['Email code']);
  });

  it('shows a no-methods state, without a flow or fabricated challenge', async () => {
    await identify('account.d@example.test');
    await flush(
      http.expectOne('/api/auth/identify'),
      response({ flowId: null, expiresAt: null, selectedChallenge: null, alternatives: [] }),
    );
    expect(q('[data-testid=no-methods]')).not.toBeNull();
    expect(q('[data-testid=selected-name]')).toBeNull();
  });

  it('shows the expired state when the flow lifetime has passed and disables alternatives', async () => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), response({ expiresAt: past() }));
    expect(q('[data-testid=expired]')).not.toBeNull();
    const buttons = Array.from(root.querySelectorAll('[data-testid=alternatives] button')) as HTMLButtonElement[];
    expect(buttons.every((b) => b.disabled)).toBe(true);
  });

  it.each([
    [410, 'expired', true],
    [404, 'no longer exists', true],
    [409, 'does not allow', false],
  ])('maps select status %i to a clear message', async (status, fragment, restart) => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), response());
    (root.querySelector('[data-testid=alternatives] button') as HTMLButtonElement).click();
    http.expectOne('/api/auth/flows/flow-1/select').flush({ title: 'x' }, { status, statusText: 'x' });
    await fixture.whenStable();
    expect(text('[data-testid=error]')).toContain(fragment);
    // On restartable errors the dead challenge is removed and the identify form returns.
    expect(q('#identifier') !== null).toBe(restart);
  });

  it('shows a message for an unknown account', async () => {
    await identify('nobody@example.test');
    http.expectOne('/api/auth/identify').flush({ detail: 'x' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    expect(text('[data-testid=error]')).toContain('No account matches');
    expect(q('#identifier')).not.toBeNull();
  });

  it('shows the server validation message for a 400', async () => {
    await identify('');
    http.expectOne('/api/auth/identify').flush({ detail: 'Identifier is required.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    expect(text('[data-testid=error]')).toContain('Identifier is required.');
  });

  it('reports an unreachable backend', async () => {
    await identify();
    http.expectOne('/api/auth/identify').error(new ProgressEvent('error'), { status: 0 });
    await fixture.whenStable();
    expect(text('[data-testid=error]')).toContain('Cannot reach the authentication server');
  });

  it('rejects a malformed 200 response instead of rendering it', async () => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), { hello: 'world' });
    expect(text('[data-testid=error]')).toContain('does not understand');
    expect(q('[data-testid=selected-name]')).toBeNull();
  });

  it('degrades gracefully for an unsupported selected challenge type', async () => {
    await identify();
    await flush(
      http.expectOne('/api/auth/identify'),
      response({
        selectedChallenge: { type: 'Passkey', displayName: 'Passkey', description: 'Use your passkey.' },
      }),
    );
    expect(text('[data-testid=selected-name]')).toBe('Passkey');
    expect(q('[data-testid=unsupported-selected]')).not.toBeNull();
    // Supported alternatives remain usable.
    expect((root.querySelector('[data-testid=alternatives] button') as HTMLButtonElement).disabled).toBe(false);
  });

  it('disables unsupported alternatives so the client never requests them', async () => {
    await identify();
    await flush(
      http.expectOne('/api/auth/identify'),
      response({ alternatives: [{ type: 'Passkey', displayName: 'Passkey', description: 'x' }] }),
    );
    const btn = root.querySelector('[data-testid=alternatives] button') as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(root.querySelector('[data-testid=alternatives]')?.textContent).toContain('not supported');
  });

  it('start over returns to the identify form', async () => {
    await identify();
    await flush(http.expectOne('/api/auth/identify'), response());
    (root.querySelector('.link') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(q('#identifier')).not.toBeNull();
  });
});
