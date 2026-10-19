import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, map, throwError } from 'rxjs';
import {
  ApiError,
  Challenge,
  ChallengeResponse,
  IdentifyRequest,
  ProblemDetails,
  SelectChallengeRequest,
} from '../models/auth.models';
import { API_BASE_URL } from './api-config';

type Operation = 'identify' | 'select';

/** Thin typed wrapper over the backend. It contains no policy logic. */
@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  identify(request: IdentifyRequest): Observable<ChallengeResponse> {
    return this.http.post<unknown>(`${this.baseUrl}/api/auth/identify`, request).pipe(
      map(validateResponse),
      catchError((e) => throwError(() => toApiError(e, 'identify'))),
    );
  }

  selectChallenge(flowId: string, type: string): Observable<ChallengeResponse> {
    const body: SelectChallengeRequest = { authenticationType: type };
    return this.http
      .post<unknown>(`${this.baseUrl}/api/auth/flows/${encodeURIComponent(flowId)}/select`, body)
      .pipe(
        map(validateResponse),
        catchError((e) => throwError(() => toApiError(e, 'select'))),
      );
  }
}

function isChallenge(value: unknown): value is Challenge {
  const c = value as Challenge;
  return (
    !!c && typeof c.type === 'string' && typeof c.displayName === 'string' && typeof c.description === 'string'
  );
}

/** Rejects payloads that do not match the contract instead of rendering guesses. */
export function validateResponse(value: unknown): ChallengeResponse {
  const r = value as ChallengeResponse;
  const ok =
    !!r &&
    typeof r === 'object' &&
    Array.isArray(r.alternatives) &&
    r.alternatives.every(isChallenge) &&
    (r.selectedChallenge === null || isChallenge(r.selectedChallenge)) &&
    (r.flowId === null || typeof r.flowId === 'string') &&
    (r.expiresAt === null || typeof r.expiresAt === 'string') &&
    // A selected challenge without a flow (or vice versa) is not a valid decision.
    (r.selectedChallenge === null) === (r.flowId === null);
  if (!ok) {
    throw <ApiError>{
      kind: 'unexpected-response',
      message: 'The server returned a response this client does not understand.',
      restartRequired: false,
    };
  }
  return { ...r, notice: typeof r.notice === 'string' ? r.notice : '' };
}

export function toApiError(error: unknown, operation: Operation): ApiError {
  if ((error as ApiError)?.kind) {
    return error as ApiError;
  }
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'unexpected-error', message: 'An unexpected error occurred.', restartRequired: false };
  }
  const detail = (error.error as ProblemDetails | null)?.detail;
  switch (error.status) {
    case 0:
    case 502:
    case 503:
    case 504:
      return {
        kind: 'backend-unavailable',
        message: 'Cannot reach the authentication server. Check that the backend is running and try again.',
        restartRequired: false,
      };
    case 400:
      return {
        kind: 'invalid-request',
        message: detail ?? 'The request was not valid. Check the values you entered.',
        restartRequired: false,
      };
    case 404:
      return operation === 'select'
        ? {
            kind: 'unknown-flow',
            message: 'This authentication flow no longer exists on the server.',
            restartRequired: true,
          }
        : {
            kind: 'unknown-account',
            message: 'No account matches that identifier.',
            restartRequired: false,
          };
    case 409:
      return {
        kind: 'method-not-allowed',
        message: 'The server does not allow that method for this flow.',
        restartRequired: false,
      };
    case 410:
      return {
        kind: 'flow-expired',
        message: 'This authentication flow has expired. Start a new one.',
        restartRequired: true,
      };
    default:
      return {
        kind: 'unexpected-error',
        message: `The server returned an unexpected error (${error.status}).`,
        restartRequired: false,
      };
  }
}
