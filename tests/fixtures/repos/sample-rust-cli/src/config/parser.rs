use std::collections::HashMap;
use std::fs;
use std::path::Path;

use super::error::ConfigError;

/// Configuration parser that reads key-value pairs from a TOML-like file.
/// BUG: Panics when encountering unknown keys instead of returning an error.
pub struct ConfigParser {
    values: HashMap<String, String>,
}

impl ConfigParser {
    pub fn new() -> Self {
        ConfigParser {
            values: HashMap::new(),
        }
    }

    pub fn parse_file(path: &Path) -> Result<Self, ConfigError> {
        let content = fs::read_to_string(path)
            .map_err(|e| ConfigError::IoError(e.to_string()))?;
        Self::parse_str(&content)
    }

    pub fn parse_str(content: &str) -> Result<Self, ConfigError> {
        let mut parser = ConfigParser::new();
        for (line_num, line) in content.lines().enumerate() {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }
            parser.parse_line(line, line_num)?;
        }
        Ok(parser)
    }

    fn parse_line(&mut self, line: &str, line_num: usize) -> Result<(), ConfigError> {
        if let Some(eq_pos) = line.find('=') {
            let key = line[..eq_pos].trim().to_string();
            let value = line[eq_pos + 1..].trim().to_string();

            // BUG: panics on unknown keys instead of returning error
            if !Self::is_known_key(&key) {
                panic!("Unknown configuration key: '{}' at line {}", key, line_num);
            }

            self.values.insert(key, value);
        } else {
            return Err(ConfigError::ParseError(format!(
                "Invalid syntax at line {}: expected 'key = value'",
                line_num
            )));
        }
        Ok(())
    }

    fn is_known_key(key: &str) -> bool {
        matches!(
            key,
            "port" | "host" | "workers" | "timeout" | "debug" | "log_level" | "db_url"
        )
    }

    pub fn get(&self, key: &str) -> Option<&str> {
        self.values.get(key).map(|s| s.as_str())
    }

    pub fn get_or_default(&self, key: &str, default: &str) -> String {
        self.get(key).unwrap_or(default).to_string()
    }

    pub fn get_as_int(&self, key: &str) -> Result<Option<i64>, ConfigError> {
        match self.get(key) {
            Some(val) => {
                let parsed = val
                    .parse::<i64>()
                    .map_err(|_| ConfigError::ParseError(format!("'{}' is not a valid integer", val)))?;
                Ok(Some(parsed))
            }
            None => Ok(None),
        }
    }
}
