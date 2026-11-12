<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                version="1.0">
  <xsl:output method="xml" indent="yes"/>
  <xsl:template match="/parameters">
    <w:document>
      <w:body>
        <w:p>
          <w:pPr><w:jc w:val="center"/></w:pPr>
          <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">УВЕДОМЛЕНИЕ О ПОСТУПЛЕНИИ ОТПРАВЛЕНИЯ</w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Name"/></w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Address_Postal"/></w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:t xml:space="preserve">Уведомляем Вас о поступлении на наш адрес почтового отправления, адресованного Вашей организации.</w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:t xml:space="preserve">Отправление ожидает получения в течение срока хранения. Для получения необходимо предъявить документ, удостоверяющий личность, и доверенность (при получении представителем).</w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_DirectorFio"/></w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:t xml:space="preserve">(подпись уполномоченного лица)</w:t></w:r>
        </w:p>
      </w:body>
    </w:document>
  </xsl:template>
</xsl:stylesheet>
