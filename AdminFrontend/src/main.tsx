import React from 'react'
import ReactDOM from 'react-dom/client'
import { App as AntApp, ConfigProvider } from 'antd'
import plPL from 'antd/locale/pl_PL'
import 'antd/dist/reset.css'
import './styles.css'
import { App } from './App'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <ConfigProvider
      locale={plPL}
      theme={{
        token: {
          colorPrimary: '#7461dc',
          colorInfo: '#7461dc',
          colorSuccess: '#388c73',
          colorText: '#28334a',
          colorTextSecondary: '#7c8598',
          colorBorder: '#e6e9f0',
          colorBgLayout: '#f6f7fb',
          borderRadius: 9,
          controlHeight: 38,
          fontFamily: "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
        },
        components: {
          Button: { fontWeight: 500 },
          Menu: { itemSelectedBg: '#f0edfc', itemSelectedColor: '#7461dc' },
          Table: { headerBg: '#fafbfe' },
          Calendar: { itemActiveBg: '#f0edfc' },
        },
      }}
    >
      <AntApp>
        <App />
      </AntApp>
    </ConfigProvider>
  </React.StrictMode>,
)
