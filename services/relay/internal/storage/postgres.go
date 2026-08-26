package storage

import (
	"context"
	"crypto/subtle"
	"time"

	"github.com/jackc/pgx/v5"
	"github.com/jackc/pgx/v5/pgxpool"
)

type PostgresStore struct {
	pool *pgxpool.Pool
}

func NewPostgresStore(ctx context.Context, databaseURL string) (*PostgresStore, error) {
	pool, err := pgxpool.New(ctx, databaseURL)
	if err != nil {
		return nil, err
	}
	if err := pool.Ping(ctx); err != nil {
		pool.Close()
		return nil, err
	}
	return &PostgresStore{pool: pool}, nil
}

func (s *PostgresStore) CreatePairing(ctx context.Context, deviceID, deviceName, code string, expiresAt time.Time, bridgeTokenHash []byte) error {
	tx, err := s.pool.Begin(ctx)
	if err != nil {
		return err
	}
	defer func() { _ = tx.Rollback(ctx) }()

	if _, err = tx.Exec(ctx,
		`insert into devices (id, name, created_at) values ($1, $2, now())`,
		deviceID, deviceName); err != nil {
		return err
	}
	if _, err = tx.Exec(ctx,
		`insert into device_credentials (device_id, role, token_hash, created_at) values ($1, 'bridge', $2, now())`,
		deviceID, bridgeTokenHash); err != nil {
		return err
	}
	if _, err = tx.Exec(ctx,
		`insert into pairing_sessions (code, device_id, expires_at, created_at) values ($1, $2, $3, now())`,
		code, deviceID, expiresAt); err != nil {
		return err
	}
	return tx.Commit(ctx)
}

func (s *PostgresStore) ClaimPairing(ctx context.Context, code string, webTokenHash []byte, now time.Time) (string, error) {
	tx, err := s.pool.Begin(ctx)
	if err != nil {
		return "", err
	}
	defer func() { _ = tx.Rollback(ctx) }()

	var deviceID string
	var expiresAt time.Time
	var claimedAt *time.Time
	err = tx.QueryRow(ctx,
		`select device_id, expires_at, claimed_at from pairing_sessions where code = $1 for update`, code).
		Scan(&deviceID, &expiresAt, &claimedAt)
	if err == pgx.ErrNoRows {
		return "", ErrPairingNotFound
	}
	if err != nil {
		return "", err
	}
	if claimedAt != nil {
		return "", ErrPairingClaimed
	}
	if now.After(expiresAt) {
		return "", ErrPairingExpired
	}

	if _, err = tx.Exec(ctx,
		`update pairing_sessions set claimed_at = $2 where code = $1`, code, now); err != nil {
		return "", err
	}
	if _, err = tx.Exec(ctx,
		`insert into device_credentials (device_id, role, token_hash, created_at) values ($1, 'web', $2, now())`,
		deviceID, webTokenHash); err != nil {
		return "", err
	}
	if _, err = tx.Exec(ctx,
		`update devices set paired_at = $2 where id = $1`, deviceID, now); err != nil {
		return "", err
	}
	return deviceID, tx.Commit(ctx)
}

func (s *PostgresStore) Authenticate(ctx context.Context, deviceID, role string, tokenHash []byte) (bool, error) {
	var expected []byte
	err := s.pool.QueryRow(ctx,
		`select token_hash from device_credentials where device_id = $1 and role = $2 and revoked_at is null`,
		deviceID, role).Scan(&expected)
	if err == pgx.ErrNoRows {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	return len(expected) == len(tokenHash) && subtle.ConstantTimeCompare(expected, tokenHash) == 1, nil
}

func (s *PostgresStore) Close() error {
	s.pool.Close()
	return nil
}
