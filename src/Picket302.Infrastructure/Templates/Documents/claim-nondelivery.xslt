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
          <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">ПРЕТЕНЗИЯ О НЕДОСТАВКЕ ОТПРАВЛЕНИЯ</w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Name"/></w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Address_Legal"/></w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:t xml:space="preserve">Сообщаем, что отправление, принятое к пересылке в адрес Вашей организации, не доставлено в установленный срок.</w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:t xml:space="preserve">На основании договора о пересылке просим обеспечить доставку либо предоставить письменное разъяснение о местонахождении отправления.</w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:t xml:space="preserve">Реквизиты для связи:</w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Phone"/></w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Email"/></w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Position"/></w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_DirectorFio"/></w:t></w:r>
        </w:p>
      </w:body>
    </w:document>
  </xsl:template>
</xsl:stylesheet>
