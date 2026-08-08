use std::fmt;

pub struct Connection {
    url: String,
    connected: bool,
}

impl Connection {
    pub fn connect(url: &str) -> Result<Self, String> {
        // Simulated connection
        Ok(Connection {
            url: url.to_string(),
            connected: true,
        })
    }

    pub fn is_connected(&self) -> bool {
        self.connected
    }

    pub fn close(&mut self) {
        self.connected = false;
    }

    pub fn url(&self) -> &str {
        &self.url
    }
}

impl fmt::Debug for Connection {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.debug_struct("Connection")
            .field("url", &self.url)
            .field("connected", &self.connected)
            .finish()
    }
}
