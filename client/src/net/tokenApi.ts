/**
 * Personal access tokens (docs/PAT-AND-MCP.md, Part 1).
 *
 * A token is what lets something other than a browser reach the builder API - an MCP server, a
 * script - because the session cookie is HttpOnly and minting one takes a password. Every call
 * here is cookie-authorised: a token cannot reach these endpoints at all, deliberately, so it can
 * never mint another one.
 */

import { request } from './api'

export interface AccessToken {
  id: string
  name: string
  scope: TokenScope
  createdAt: string
  expiresAt: string
  /** Null until the token authenticates something. Coarse - the server writes it hourly at most. */
  lastUsedAt: string | null
  /** Expired tokens stay in the list so their owner can see and clear them. */
  isExpired: boolean
}

export interface AccessTokenList {
  tokens: AccessToken[]
  /** The server's ceiling, so the form cannot offer a life it would refuse. */
  maxLifetimeDays: number
  maxTokens: number
}

/** The secret is in this response and in no other, ever. */
export interface CreatedAccessToken {
  token: AccessToken
  secret: string
}

export const TOKEN_SCOPES = ['BuilderRead', 'BuilderWrite'] as const
export type TokenScope = (typeof TOKEN_SCOPES)[number]

export const SCOPE_BLURBS: Record<TokenScope, string> = {
  BuilderRead: 'Reads the builder API. Cannot change anything.',
  BuilderWrite: 'Reads and edits content. Cannot moderate, administer, or play.',
}

export const tokenApi = {
  list: () => request<AccessTokenList>('/api/auth/tokens'),

  create: (name: string, scope: TokenScope, expiresInDays: number) =>
    request<CreatedAccessToken>('/api/auth/tokens', {
      method: 'POST',
      body: JSON.stringify({ name, scope, expiresInDays }),
    }),

  revoke: (id: string) => request<void>(`/api/auth/tokens/${id}`, { method: 'DELETE' }),
}
