-- FOUNDERS cloud save: database and tables (MariaDB 10.5+ / MySQL 8+).
-- Run once as the database administrator:  sudo mysql < schema.sql
CREATE DATABASE IF NOT EXISTS founders CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE founders;

-- One row per player: a guest of a device until a username and password are bound.
CREATE TABLE IF NOT EXISTS players (
  id          CHAR(32)     NOT NULL PRIMARY KEY,
  username    VARCHAR(20)  NULL UNIQUE,
  pass_hash   VARCHAR(255) NULL,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_seen   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;

-- Sign-in tokens (only their SHA-256 is stored).
CREATE TABLE IF NOT EXISTS sessions (
  token_hash  CHAR(64)     NOT NULL PRIMARY KEY,
  player_id   CHAR(32)     NOT NULL,
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  last_used   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX (player_id),
  FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- Saved values per player (the game keeps its whole settlement under one key).
CREATE TABLE IF NOT EXISTS saves (
  player_id   CHAR(32)     NOT NULL,
  save_key    VARCHAR(64)  NOT NULL,
  value       LONGTEXT     NOT NULL,
  updated_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (player_id, save_key),
  FOREIGN KEY (player_id) REFERENCES players(id) ON DELETE CASCADE
) ENGINE=InnoDB;
