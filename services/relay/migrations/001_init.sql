create table if not exists users (
    id uuid primary key,
    created_at timestamptz not null default now()
);

create table if not exists devices (
    id uuid primary key,
    user_id uuid references users(id),
    name text not null,
    created_at timestamptz not null default now(),
    paired_at timestamptz,
    last_seen_at timestamptz
);

create table if not exists device_credentials (
    id bigserial primary key,
    device_id uuid not null references devices(id) on delete cascade,
    role text not null check (role in ('bridge', 'web')),
    token_hash bytea not null,
    created_at timestamptz not null default now(),
    revoked_at timestamptz,
    unique (device_id, role)
);

create table if not exists pairing_sessions (
    code text primary key,
    device_id uuid not null references devices(id) on delete cascade,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    claimed_at timestamptz
);

create table if not exists pending_commands (
    request_id uuid primary key,
    device_id uuid not null references devices(id) on delete cascade,
    command_type text not null,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null,
    status text not null,
    constraint no_prompt_payload check (true)
);

comment on table pending_commands is 'Metadata only. Never store prompts, source code, or full WebSocket payloads.';
